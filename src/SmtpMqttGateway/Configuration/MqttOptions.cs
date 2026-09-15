namespace SmtpMqttGateway.Configuration;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public string ClientId { get; set; } = "homelab-smtp-gateway";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool UseTls { get; set; }

    /// <summary>
    /// Fixed fan-out topic every alert is published to, regardless of sender.
    /// </summary>
    public string RawTopic { get; set; } = "homelab/alerts/raw";

    /// <summary>
    /// Template for a second, per-sender publish, so different senders can be
    /// routed to different downstream flows without needing external
    /// classification logic. Literal text is used as-is; "{variable}"
    /// placeholders are substituted per message. Supported variables:
    /// sender, senderLocal, senderDomain, recipient, recipientLocal,
    /// recipientDomain (recipient* refer to the first envelope recipient).
    /// Substituted values are sanitized for MQTT topic safety (lowercased;
    /// '/', '+', '#', whitespace and control characters replaced with '_').
    /// </summary>
    public string TopicTemplate { get; set; } = "homelab/alerts/{senderLocal}";

    public string StatusTopic { get; set; } = "homelab/gateways/smtp/status";

    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int PublishTimeoutSeconds { get; set; } = 15;

    public int ReconnectDelaySeconds { get; set; } = 5;
}
