using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Server;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Models;
using SmtpMqttGateway.Mqtt;
using SmtpMqttGateway.Tests.TestSupport;

namespace SmtpMqttGateway.Tests.Mqtt;

/// <summary>
/// Exercises MqttAlertPublisher against a real, in-process MQTTnet broker
/// (MQTTnet.Server) rather than a mock IMqttClient, so the QoS 1 / retained /
/// LWT wiring is verified against the real protocol behaviour.
/// </summary>
public sealed class MqttAlertPublisherTests : IAsyncLifetime
{
    private MqttServer? _broker;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = GetFreeTcpPort();

        var serverFactory = new MqttServerFactory();
        var serverOptions = serverFactory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(_port)
            .Build();

        // This sandbox has no IPv6 stack at all, so skip binding the IPv6
        // listener MQTTnet.Server otherwise opens alongside the IPv4 one.
        // MQTTnet.Server.Internal.Adapter.MqttTcpServerAdapter treats
        // IPAddress.None (not null) as "do not bind this address family".
        serverOptions.DefaultEndpointOptions.BoundInterNetworkV6Address = System.Net.IPAddress.None;

        _broker = serverFactory.CreateMqttServer(serverOptions);
        await _broker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_broker is not null)
        {
            await _broker.StopAsync(new MqttServerStopOptionsBuilder().Build());
            _broker.Dispose();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private MqttOptions BuildOptions(string password = "") => new()
    {
        Host = "127.0.0.1",
        Port = _port,
        ClientId = "gateway-under-test",
        Username = "smtp-gateway",
        Password = password,
        Topic = "homelab/alerts/raw",
        StatusTopic = "homelab/gateways/smtp/status",
        ConnectTimeoutSeconds = 5,
        PublishTimeoutSeconds = 5,
        ReconnectDelaySeconds = 1,
    };

    private static AlertEnvelopeV1 SampleEvent() => new()
    {
        EventId = "abc123",
        ReceivedAtUtc = DateTimeOffset.UtcNow,
        Envelope = new EnvelopeInfo { From = "root@example.com", Recipients = ["alerts@example.com"] },
        Headers = new HeaderInfo { Subject = "Test" },
        Content = new ContentInfo { Text = "hello" },
    };

    private async Task<IMqttClient> ConnectSubscriberAsync(string topicFilter, List<MqttApplicationMessageReceivedEventArgs> received)
    {
        var factory = new MqttClientFactory();
        var subscriber = factory.CreateMqttClient();
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            received.Add(args);
            return Task.CompletedTask;
        };

        var options = factory.CreateClientOptionsBuilder()
            .WithClientId("test-subscriber")
            .WithTcpServer("127.0.0.1", _port)
            .Build();

        await subscriber.ConnectAsync(options);
        await subscriber.SubscribeAsync(factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicFilter).WithAtLeastOnceQoS())
            .Build());

        return subscriber;
    }

    [Fact]
    public async Task SuccessfulPublish_IsAcknowledgedAndDeliveredNonRetained()
    {
        var received = new List<MqttApplicationMessageReceivedEventArgs>();
        using var subscriber = await ConnectSubscriberAsync("homelab/alerts/raw", received);

        await using var publisher = new MqttAlertPublisher(Options.Create(BuildOptions()), NullLogger());
        await publisher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => received.Count == 0, TimeSpan.FromSeconds(1)); // just let it connect

        var result = await WaitForResultAsync(publisher);
        Assert.True(result);

        await WaitUntilAsync(() => received.Any(r => r.ApplicationMessage.Topic == "homelab/alerts/raw"), TimeSpan.FromSeconds(5));

        var alertMessage = received.Single(r => r.ApplicationMessage.Topic == "homelab/alerts/raw");
        Assert.False(alertMessage.ApplicationMessage.Retain);
        Assert.Equal(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, alertMessage.ApplicationMessage.QualityOfServiceLevel);

        var json = Encoding.UTF8.GetString(alertMessage.ApplicationMessage.Payload.ToArray());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("abc123", doc.RootElement.GetProperty("eventId").GetString());

        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Connect_PublishesRetainedOnlineStatus()
    {
        await using var publisher = new MqttAlertPublisher(Options.Create(BuildOptions()), NullLogger());
        await publisher.StartAsync(CancellationToken.None);

        await WaitUntilAsync(async () =>
        {
            var retained = await _broker!.GetRetainedMessagesAsync();
            return retained.Any(m => m.Topic == "homelab/gateways/smtp/status");
        }, TimeSpan.FromSeconds(5));

        var retainedMessages = await _broker!.GetRetainedMessagesAsync();
        var status = retainedMessages.Single(m => m.Topic == "homelab/gateways/smtp/status");
        Assert.True(status.Retain);

        var json = Encoding.UTF8.GetString(status.Payload.ToArray());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("online", doc.RootElement.GetProperty("status").GetString());

        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GracefulShutdown_PublishesRetainedOfflineStatus()
    {
        var publisher = new MqttAlertPublisher(Options.Create(BuildOptions()), NullLogger());
        await publisher.StartAsync(CancellationToken.None);

        await WaitUntilAsync(async () =>
        {
            var retained = await _broker!.GetRetainedMessagesAsync();
            return retained.Any(m => m.Topic == "homelab/gateways/smtp/status");
        }, TimeSpan.FromSeconds(5));

        await publisher.StopAsync(CancellationToken.None);

        var retainedMessages = await _broker!.GetRetainedMessagesAsync();
        var status = retainedMessages.Single(m => m.Topic == "homelab/gateways/smtp/status");
        var json = Encoding.UTF8.GetString(status.Payload.ToArray());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("offline", doc.RootElement.GetProperty("status").GetString());

        await publisher.DisposeAsync();
    }

    [Fact]
    public async Task BrokerUnavailable_PublishReturnsFalseWithoutThrowing()
    {
        var options = BuildOptions();
        options.Port = GetFreeTcpPort(); // nothing listening here
        options.ConnectTimeoutSeconds = 1;
        options.ReconnectDelaySeconds = 1;

        await using var publisher = new MqttAlertPublisher(Options.Create(options), NullLogger());
        await publisher.StartAsync(CancellationToken.None);

        var result = await publisher.PublishAlertAsync(SampleEvent(), CancellationToken.None);

        Assert.False(result);

        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PasswordNeverAppearsInLogs()
    {
        const string secret = "super-secret-mqtt-password";
        var loggerProvider = new ListLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger<MqttAlertPublisher>();

        var options = BuildOptions(secret);

        await using (var publisher = new MqttAlertPublisher(Options.Create(options), logger))
        {
            await publisher.StartAsync(CancellationToken.None);
            await WaitForResultAsync(publisher);
            await publisher.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(loggerProvider.Lines, line => line.Contains(secret, StringComparison.Ordinal));
    }

    private static ILogger<MqttAlertPublisher> NullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<MqttAlertPublisher>.Instance;

    private static async Task<bool> WaitForResultAsync(MqttAlertPublisher publisher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var result = await publisher.PublishAlertAsync(SampleEvent(), CancellationToken.None);
            if (result)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }
    }
}
