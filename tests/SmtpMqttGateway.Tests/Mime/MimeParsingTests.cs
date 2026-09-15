using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Services;

namespace SmtpMqttGateway.Tests.Mime;

public class MimeParsingTests
{
    private static AlertEventFactory CreateFactory(SmtpOptions? options = null) =>
        new(Options.Create(options ?? new SmtpOptions()));

    private static byte[] ToBytes(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.ToArray();
    }

    [Fact]
    public void PlainTextMessage_ExtractsTextBody()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Disk failure";
        message.Body = new TextPart("plain") { Text = "Disk sda failed." };

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Equal("Disk sda failed.", evt.Content.Text);
        Assert.Null(evt.Content.Html);
        Assert.Equal("Disk failure", evt.Headers.Subject);
    }

    [Fact]
    public void HtmlOnlyMessage_DerivesPlainTextFromHtml()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Pool degraded";
        message.Body = new TextPart("html") { Text = "<html><body><p>Pool <b>tank</b> is DEGRADED</p></body></html>" };

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Contains("Pool tank is DEGRADED", evt.Content.Text);
        Assert.Contains("<b>tank</b>", evt.Content.Html);
    }

    [Fact]
    public void MultipartAlternative_PrefersTextBodyOverHtml()
    {
        var text = new TextPart("plain") { Text = "Plain version" };
        var html = new TextPart("html") { Text = "<p>HTML version</p>" };
        var alternative = new MultipartAlternative { text, html };

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Alternative body";
        message.Body = alternative;

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Equal("Plain version", evt.Content.Text);
        Assert.Contains("HTML version", evt.Content.Html);
    }

    [Fact]
    public void MultipartMixed_ExtractsBodyAndAttachmentMetadata()
    {
        var textPart = new TextPart("plain") { Text = "See attached report." };
        var attachment = new MimePart("text", "plain")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("report contents"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "report.txt",
        };

        var mixed = new Multipart("mixed") { textPart, attachment };

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Report attached";
        message.Body = mixed;

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Equal("See attached report.", evt.Content.Text);
        var attachmentInfo = Assert.Single(evt.Attachments);
        Assert.Equal("report.txt", attachmentInfo.FileName);
        Assert.Equal("text/plain", attachmentInfo.MediaType);
        Assert.Equal(Encoding.UTF8.GetByteCount("report contents"), attachmentInfo.Size);
    }

    [Fact]
    public void EncodedUtf8Subject_IsDecodedCorrectly()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Pool état dégradé";
        message.Body = new TextPart("plain") { Text = "body" };

        var raw = ToBytes(message);
        var reparsed = MimeMessage.Load(new MemoryStream(raw));

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], raw, reparsed, DateTimeOffset.UtcNow);

        Assert.Equal("Pool état dégradé", evt.Headers.Subject);
    }

    [Fact]
    public void MissingSubject_ProducesNullSubject()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Body = new TextPart("plain") { Text = "body" };

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Null(evt.Headers.Subject);
    }

    [Fact]
    public void EmptyBody_ProducesNullTextAndHtml()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Empty";
        message.Body = new TextPart("plain") { Text = string.Empty };

        var factory = CreateFactory();
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Null(evt.Content.Text);
        Assert.Null(evt.Content.Html);
    }

    [Fact]
    public void EnvelopeAndHeaderAddresses_CanDiffer()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("TrueNAS", "root@truenas.local"));
        message.To.Add(new MailboxAddress("Me", "my-existing-address@gmail.com"));
        message.Subject = "Pool state changed";
        message.Body = new TextPart("plain") { Text = "body" };

        var factory = CreateFactory();
        var evt = factory.Create(
            "smtp-alerts@internal.invalid",
            ["alerts@notify.home.arpa"],
            ToBytes(message),
            message,
            DateTimeOffset.UtcNow);

        Assert.Equal("smtp-alerts@internal.invalid", evt.Envelope.From);
        Assert.Equal(["alerts@notify.home.arpa"], evt.Envelope.Recipients);
        Assert.Contains("root@truenas.local", evt.Headers.From);
        Assert.Contains("my-existing-address@gmail.com", evt.Headers.To[0]);
    }

    [Fact]
    public void AttachmentCount_TruncatedBeyondConfiguredLimit()
    {
        var mixed = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "body" },
        };

        for (var i = 0; i < 5; i++)
        {
            mixed.Add(new MimePart("application", "octet-stream")
            {
                Content = new MimeContent(new MemoryStream([1, 2, 3])),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                FileName = $"file{i}.bin",
            });
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Body = mixed;

        var factory = CreateFactory(new SmtpOptions { MaxAttachmentCount = 2 });
        var evt = factory.Create("alerter@example.com", ["ops@example.com"], ToBytes(message), message, DateTimeOffset.UtcNow);

        Assert.Equal(2, evt.Attachments.Count);
        Assert.True(evt.AttachmentsTruncated);
    }

    [Fact]
    public void DeterministicEventId_SameInputsProduceSameId()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Repeat";
        message.Body = new TextPart("plain") { Text = "same content" };

        var raw = ToBytes(message);
        var factory = CreateFactory();

        var first = factory.Create("alerter@example.com", ["ops@example.com"], raw, message, DateTimeOffset.UtcNow);
        var second = factory.Create("alerter@example.com", ["ops@example.com"], raw, message, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(first.EventId, second.EventId);
    }

    [Fact]
    public void DeterministicEventId_DifferentEnvelopeProducesDifferentId()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Repeat";
        message.Body = new TextPart("plain") { Text = "same content" };

        var raw = ToBytes(message);
        var factory = CreateFactory();

        var first = factory.Create("alerter@example.com", ["ops@example.com"], raw, message, DateTimeOffset.UtcNow);
        var second = factory.Create("alerter@example.com", ["other@example.com"], raw, message, DateTimeOffset.UtcNow);

        Assert.NotEqual(first.EventId, second.EventId);
    }
}
