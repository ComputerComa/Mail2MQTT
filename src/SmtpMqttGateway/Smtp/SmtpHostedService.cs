using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpMqttGateway.Configuration;
using SmtpServer;
using SmtpServer.Storage;

namespace SmtpMqttGateway.Smtp;

/// <summary>
/// Hosts the loopback-only SMTP listener that Postfix relays into. It never
/// terminates TLS or authenticates clients itself - Postfix already did both
/// before it queues the message for local delivery to this listener.
/// </summary>
public sealed class SmtpHostedService : IHostedService, IDisposable
{
    private readonly SmtpServer.SmtpServer _server;
    private readonly ILogger<SmtpHostedService> _logger;
    private readonly SmtpOptions _options;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public SmtpHostedService(IOptions<SmtpOptions> options, IMessageStore messageStore, ILogger<SmtpHostedService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var serverOptions = new SmtpServerOptionsBuilder()
            .ServerName(_options.ServerName)
            .Endpoint(endpoint => endpoint
                .Endpoint(new IPEndPoint(IPAddress.Parse(_options.ListenAddress), _options.Port))
                .AuthenticationRequired(false)
                .AllowUnsecureAuthentication(true)
                .IsSecure(false))
            .MaxMessageSize(_options.MaxMessageBytes, MaxMessageSizeHandling.Strict)
            .Build();

        var serviceProvider = new SmtpServer.ComponentModel.ServiceProvider();
        serviceProvider.Add(messageStore);

        _server = new SmtpServer.SmtpServer(serverOptions, serviceProvider);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _serverTask = _server.StartAsync(_cts.Token);

        _logger.LogInformation(
            "SMTP listener started on {Address}:{Port}, maxMessageBytes={MaxMessageBytes}",
            _options.ListenAddress, _options.Port, _options.MaxMessageBytes);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SMTP listener stopping, no longer accepting new connections");

        _server.Shutdown();

        if (_serverTask is not null)
        {
            try
            {
                await Task.WhenAny(_server.ShutdownTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Host shutdown timeout elapsed; proceed with disposal below.
            }
        }

        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
