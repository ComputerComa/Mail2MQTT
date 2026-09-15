using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Models;

namespace SmtpMqttGateway.Services;

public sealed class AlertEventFactory(IOptions<SmtpOptions> smtpOptions) : IAlertEventFactory
{
    private readonly SmtpOptions _options = smtpOptions.Value;

    public AlertEnvelopeV1 Create(
        string envelopeFrom,
        IReadOnlyList<string> envelopeRecipients,
        ReadOnlyMemory<byte> rawMessage,
        MimeMessage message,
        DateTimeOffset receivedAtUtc)
    {
        var eventId = ComputeEventId(rawMessage.Span, envelopeFrom, envelopeRecipients);

        var (text, html) = ExtractBody(message);
        var (truncatedText, textTruncated) = Truncate(text, _options.MaxTextLength);
        var (truncatedHtml, htmlTruncated) = Truncate(html, _options.MaxHtmlLength);

        var attachments = message.Attachments
            .OfType<MimePart>()
            .Select(DescribeAttachment)
            .ToList();

        var attachmentsTruncated = attachments.Count > _options.MaxAttachmentCount;
        var limitedAttachments = attachmentsTruncated
            ? attachments.Take(_options.MaxAttachmentCount).ToList()
            : attachments;

        var topmostReceived = message.Headers
            .FirstOrDefault(h => h.Field.Equals("Received", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return new AlertEnvelopeV1
        {
            EventId = eventId,
            ReceivedAtUtc = receivedAtUtc,
            MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId,
            Envelope = new EnvelopeInfo
            {
                From = envelopeFrom,
                Recipients = envelopeRecipients,
            },
            Headers = new HeaderInfo
            {
                From = message.From.Count > 0 ? message.From.ToString() : null,
                To = message.To.Select(a => a.ToString()).ToList(),
                Subject = string.IsNullOrWhiteSpace(message.Subject) ? null : message.Subject,
                DateUtc = message.Date == default ? null : message.Date.ToUniversalTime(),
            },
            Content = new ContentInfo
            {
                Text = truncatedText,
                Html = truncatedHtml,
                TextTruncated = textTruncated,
                HtmlTruncated = htmlTruncated,
            },
            Attachments = limitedAttachments,
            AttachmentsTruncated = attachmentsTruncated,
            SourceHint = topmostReceived is null
                ? null
                : new SourceHintInfo { TopmostReceivedHeader = topmostReceived },
        };
    }

    private static (string? Text, string? Html) ExtractBody(MimeMessage message)
    {
        var text = message.TextBody;
        var html = message.HtmlBody;

        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(html))
        {
            text = HtmlTextExtractor.ExtractText(html);
        }

        return (
            string.IsNullOrEmpty(text) ? null : text,
            string.IsNullOrEmpty(html) ? null : html);
    }

    private static AttachmentInfo DescribeAttachment(MimePart part)
    {
        using var counter = new CountingStream();
        try
        {
            part.Content?.DecodeTo(counter);
        }
        catch (Exception)
        {
            // Malformed attachment encoding: metadata is still useful even if size is unknown.
        }

        return new AttachmentInfo
        {
            FileName = string.IsNullOrWhiteSpace(part.FileName) ? null : part.FileName,
            MediaType = part.ContentType.MimeType,
            Size = counter.Length,
        };
    }

    private static string ComputeEventId(ReadOnlySpan<byte> rawMessage, string envelopeFrom, IReadOnlyList<string> recipients)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(rawMessage);
        sha256.AppendData(Encoding.UTF8.GetBytes("\0from\0"));
        sha256.AppendData(Encoding.UTF8.GetBytes(envelopeFrom));
        sha256.AppendData(Encoding.UTF8.GetBytes("\0rcpt\0"));
        sha256.AppendData(Encoding.UTF8.GetBytes(string.Join('\0', recipients)));

        var hash = sha256.GetHashAndReset();
        return Convert.ToHexStringLower(hash);
    }

    private static (string? Value, bool Truncated) Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return (value, false);
        }

        return (value[..maxLength], true);
    }

    private sealed class CountingStream : Stream
    {
        private long _length;

        public override long Length => _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override void Write(byte[] buffer, int offset, int count) => _length += count;

        public override void Flush() { }

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
