using Microsoft.Extensions.Options;

namespace SmtpMqttGateway.Configuration;

public sealed class MqttOptionsValidator : IValidateOptions<MqttOptions>
{
    public ValidateOptionsResult Validate(string? name, MqttOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Mqtt:Host must not be empty.");
        }

        if (options.Port is <= 0 or > 65535)
        {
            failures.Add($"Mqtt:Port {options.Port} must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Mqtt:ClientId must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Topic))
        {
            failures.Add("Mqtt:Topic must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.StatusTopic))
        {
            failures.Add("Mqtt:StatusTopic must not be empty.");
        }

        if (options.ConnectTimeoutSeconds <= 0)
        {
            failures.Add("Mqtt:ConnectTimeoutSeconds must be greater than zero.");
        }

        if (options.PublishTimeoutSeconds <= 0)
        {
            failures.Add("Mqtt:PublishTimeoutSeconds must be greater than zero.");
        }

        if (options.ReconnectDelaySeconds <= 0)
        {
            failures.Add("Mqtt:ReconnectDelaySeconds must be greater than zero.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
