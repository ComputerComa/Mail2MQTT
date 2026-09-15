using System.Text.Json;

namespace SmtpMqttGateway.Services;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions AlertEventOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new UtcDateTimeOffsetConverter() },
    };
}
