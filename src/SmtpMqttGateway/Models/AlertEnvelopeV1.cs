namespace SmtpMqttGateway.Models;

public sealed record AlertEnvelopeV1
{
    public int SchemaVersion { get; init; } = 1;

    public required string EventId { get; init; }

    public required DateTimeOffset ReceivedAtUtc { get; init; }

    public string? MessageId { get; init; }

    public required EnvelopeInfo Envelope { get; init; }

    public required HeaderInfo Headers { get; init; }

    public required ContentInfo Content { get; init; }

    public IReadOnlyList<AttachmentInfo> Attachments { get; init; } = [];

    public bool AttachmentsTruncated { get; init; }

    public SourceHintInfo? SourceHint { get; init; }
}

public sealed record EnvelopeInfo
{
    public required string From { get; init; }

    public required IReadOnlyList<string> Recipients { get; init; }
}

public sealed record HeaderInfo
{
    public string? From { get; init; }

    public IReadOnlyList<string> To { get; init; } = [];

    public string? Subject { get; init; }

    public DateTimeOffset? DateUtc { get; init; }
}

public sealed record ContentInfo
{
    public string? Text { get; init; }

    public string? Html { get; init; }

    public bool TextTruncated { get; init; }

    public bool HtmlTruncated { get; init; }
}

public sealed record AttachmentInfo
{
    public string? FileName { get; init; }

    public required string MediaType { get; init; }

    public required long Size { get; init; }
}

public sealed record SourceHintInfo
{
    public string? TopmostReceivedHeader { get; init; }
}
