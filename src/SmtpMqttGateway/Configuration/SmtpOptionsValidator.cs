using System.Net;
using Microsoft.Extensions.Options;

namespace SmtpMqttGateway.Configuration;

public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        var failures = new List<string>();

        if (!IPAddress.TryParse(options.ListenAddress, out var listenAddress))
        {
            failures.Add($"Smtp:ListenAddress '{options.ListenAddress}' is not a valid IP address.");
        }
        else if (!IPAddress.IsLoopback(listenAddress))
        {
            failures.Add(
                $"Smtp:ListenAddress '{options.ListenAddress}' is not a loopback address. This listener has no " +
                "authentication or TLS of its own - it must only be reachable from the trusted SMTP frontend on " +
                "the same host, so it may only bind to a loopback address (e.g. 127.0.0.1 or ::1).");
        }

        if (options.Port is <= 0 or > 65535)
        {
            failures.Add($"Smtp:Port {options.Port} must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.ServerName))
        {
            failures.Add("Smtp:ServerName must not be empty.");
        }

        if (options.MaxMessageBytes <= 0)
        {
            failures.Add("Smtp:MaxMessageBytes must be greater than zero.");
        }

        if (options.MaxTextLength <= 0)
        {
            failures.Add("Smtp:MaxTextLength must be greater than zero.");
        }

        if (options.MaxHtmlLength <= 0)
        {
            failures.Add("Smtp:MaxHtmlLength must be greater than zero.");
        }

        if (options.MaxAttachmentCount < 0)
        {
            failures.Add("Smtp:MaxAttachmentCount must not be negative.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
