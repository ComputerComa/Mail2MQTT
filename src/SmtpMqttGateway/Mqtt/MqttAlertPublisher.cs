using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Models;
using SmtpMqttGateway.Services;

namespace SmtpMqttGateway.Mqtt;

/// <summary>
/// Owns the single MQTT connection: a background loop keeps it connected and
/// republishes the retained "online" status after every (re)connect, while
/// <see cref="PublishAlertAsync"/> only ever reports success once the broker
/// has acknowledged the QoS 1 publish. It never queues alerts while offline.
/// </summary>
public sealed class MqttAlertPublisher : IAlertPublisher, IHostedService, IAsyncDisposable
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttAlertPublisher> _logger;
    private readonly IMqttClient _client;
    private CancellationTokenSource? _stoppingCts;
    private Task? _connectionLoopTask;

    public MqttAlertPublisher(IOptions<MqttOptions> options, ILogger<MqttAlertPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "MQTT publisher starting: broker {Host}:{Port} (tls={UseTls}) clientId={ClientId} rawTopic={RawTopic} topicTemplate={TopicTemplate} statusTopic={StatusTopic}",
            _options.Host, _options.Port, _options.UseTls, _options.ClientId, _options.RawTopic, _options.TopicTemplate, _options.StatusTopic);

        _stoppingCts = new CancellationTokenSource();
        _connectionLoopTask = RunConnectionLoopAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts?.Cancel();

        if (_connectionLoopTask is not null)
        {
            try
            {
                await _connectionLoopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        if (_client.IsConnected)
        {
            await PublishStatusAsync("offline", CancellationToken.None).ConfigureAwait(false);

            try
            {
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogInformation("MQTT client disconnected cleanly");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disconnecting from MQTT broker during shutdown");
            }
        }
    }

    /// <summary>
    /// Publishes every alert twice: once to the fixed <see cref="MqttOptions.RawTopic"/>
    /// fan-out topic, and once to a per-sender topic resolved from
    /// <see cref="MqttOptions.TopicTemplate"/> (see <see cref="AlertTopicTemplate"/>),
    /// so different senders can be routed to different downstream flows.
    /// Both publishes must succeed for the overall result to be true.
    /// </summary>
    public async Task<bool> PublishAlertAsync(AlertEnvelopeV1 alertEvent, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            _logger.LogWarning("MQTT publish skipped for {EventId}: client is not connected", alertEvent.EventId);
            return false;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(alertEvent, JsonDefaults.AlertEventOptions);
        var senderTopic = AlertTopicTemplate.Resolve(_options.TopicTemplate, alertEvent);

        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        publishCts.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));

        var results = await Task.WhenAll(
            PublishPayloadAsync(_options.RawTopic, payload, alertEvent.EventId, publishCts.Token),
            PublishPayloadAsync(senderTopic, payload, alertEvent.EventId, publishCts.Token)).ConfigureAwait(false);

        return results.All(success => success);
    }

    private async Task<bool> PublishPayloadAsync(string topic, byte[] payload, string eventId, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        try
        {
            var result = await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "MQTT publish to {Topic} rejected for {EventId}: {ReasonCode} {ReasonString}",
                    topic, eventId, result.ReasonCode, result.ReasonString);
            }

            return result.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("MQTT publish to {Topic} timed out for {EventId} after {Timeout}s", topic, eventId, _options.PublishTimeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT publish to {Topic} failed for {EventId}", topic, eventId);
            return false;
        }
    }

    private async Task RunConnectionLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_client.IsConnected)
            {
                try
                {
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

                    await _client.ConnectAsync(BuildClientOptions(), connectCts.Token).ConfigureAwait(false);
                    _logger.LogInformation("MQTT connection established to {Host}:{Port}", _options.Host, _options.Port);

                    await PublishStatusAsync("online", stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "MQTT connection attempt to {Host}:{Port} failed, retrying in {Delay}s",
                        _options.Host, _options.Port, _options.ReconnectDelaySeconds);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (args.ClientWasConnected)
        {
            _logger.LogWarning("MQTT connection lost: {Reason}", args.Reason);
        }

        return Task.CompletedTask;
    }

    private async Task PublishStatusAsync(string status, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new GatewayStatus { Status = status, TimestampUtc = DateTimeOffset.UtcNow },
            JsonDefaults.AlertEventOptions);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(_options.StatusTopic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(true)
            .Build();

        try
        {
            await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish '{Status}' gateway status", status);
        }
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithTimeout(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds))
            .WithCleanSession(true)
            .WithWillTopic(_options.StatusTopic)
            .WithWillPayload(JsonSerializer.SerializeToUtf8Bytes(
                new GatewayStatus { Status = "offline", TimestampUtc = DateTimeOffset.UtcNow },
                JsonDefaults.AlertEventOptions))
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            builder = builder.WithCredentials(_options.Username, _options.Password);
        }

        if (_options.UseTls)
        {
            builder = builder.WithTlsOptions(tls => tls.UseTls());
        }

        return builder.Build();
    }

    public async ValueTask DisposeAsync()
    {
        _client.DisconnectedAsync -= OnDisconnectedAsync;
        _stoppingCts?.Dispose();
        _client.Dispose();
        await Task.CompletedTask;
    }
}
