using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Services;
using SmtpMqttGateway.Smtp;
using SmtpMqttGateway.Tests.TestSupport;

namespace SmtpMqttGateway.Tests.Smtp;

/// <summary>
/// Drives the real SmtpServer stack (via SmtpHostedService) over a loopback
/// socket, because oversized-message rejection is enforced by SmtpServer's
/// own protocol pump (SmtpServer.IO.PipeReaderExtensions.ReadDotBlockAsync)
/// before GatewayMessageStore.SaveAsync is ever invoked.
/// </summary>
public sealed class SmtpOversizedMessageTests
{
    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static SmtpHostedService CreateService(SmtpOptions options, FakeAlertPublisher publisher)
    {
        var store = new GatewayMessageStore(
            new AlertEventFactory(Options.Create(options)),
            publisher,
            NullLogger<GatewayMessageStore>.Instance);

        return new SmtpHostedService(Options.Create(options), store, NullLogger<SmtpHostedService>.Instance);
    }

    [Fact]
    public async Task OversizedMessage_IsRejectedWithSizeLimitResponse()
    {
        var options = new SmtpOptions
        {
            ListenAddress = "127.0.0.1",
            Port = GetFreeTcpPort(),
            ServerName = "test-gateway",
            MaxMessageBytes = 256,
        };

        var publisher = FakeAlertPublisher.AlwaysSucceeds();
        var service = CreateService(options, publisher);
        await service.StartAsync(CancellationToken.None);

        try
        {
            await using var client = await RawSmtpClient.ConnectAsync(options.ListenAddress, options.Port);
            await client.SendAsync($"EHLO test-client");
            await client.SendAsync("MAIL FROM:<alerter@example.com>");
            await client.SendAsync("RCPT TO:<ops@example.com>");
            await client.SendAsync("DATA");

            var oversizedBody = "Subject: too big\r\n\r\n" + new string('x', 4096) + "\r\n.\r\n";
            var response = await client.SendRawAsync(oversizedBody);

            Assert.Contains("552", response);
            Assert.Empty(publisher.PublishedEvents);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WithinSizeLimit_MessageIsAcceptedEndToEnd()
    {
        var options = new SmtpOptions
        {
            ListenAddress = "127.0.0.1",
            Port = GetFreeTcpPort(),
            ServerName = "test-gateway",
            MaxMessageBytes = 1_048_576,
        };

        var publisher = FakeAlertPublisher.AlwaysSucceeds();
        var service = CreateService(options, publisher);
        await service.StartAsync(CancellationToken.None);

        try
        {
            await using var client = await RawSmtpClient.ConnectAsync(options.ListenAddress, options.Port);
            await client.SendAsync("EHLO test-client");
            await client.SendAsync("MAIL FROM:<alerter@example.com>");
            await client.SendAsync("RCPT TO:<ops@example.com>");
            await client.SendAsync("DATA");

            var body = "Subject: small alert\r\n\r\nAll good.\r\n.\r\n";
            var response = await client.SendRawAsync(body);

            Assert.StartsWith("250", response);
            Assert.Single(publisher.PublishedEvents);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
