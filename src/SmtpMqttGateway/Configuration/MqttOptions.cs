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

    public string Topic { get; set; } = "homelab/alerts/raw";

    public string StatusTopic { get; set; } = "homelab/gateways/smtp/status";

    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int PublishTimeoutSeconds { get; set; } = 15;

    public int ReconnectDelaySeconds { get; set; } = 5;
}
