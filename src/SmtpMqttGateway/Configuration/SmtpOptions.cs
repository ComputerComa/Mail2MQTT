namespace SmtpMqttGateway.Configuration;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string ListenAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 10025;

    public string ServerName { get; set; } = "smtp-gateway.internal";

    public int MaxMessageBytes { get; set; } = 1_048_576;

    public int MaxTextLength { get; set; } = 65536;

    public int MaxHtmlLength { get; set; } = 131072;

    public int MaxAttachmentCount { get; set; } = 20;
}
