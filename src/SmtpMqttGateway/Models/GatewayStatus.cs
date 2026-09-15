namespace SmtpMqttGateway.Models;

public sealed record GatewayStatus
{
    public required string Status { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }
}
