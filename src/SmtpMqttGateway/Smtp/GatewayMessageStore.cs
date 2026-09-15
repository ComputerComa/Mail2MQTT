using System.Buffers;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmtpMqttGateway.Mqtt;
using SmtpMqttGateway.Services;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace SmtpMqttGateway.Smtp;

/// <summary>
/// Enforces the SMTP/MQTT success contract: a message is only accepted (250)
/// once MQTT has acknowledged the QoS 1 publish. Anything else - a parse
/// failure, or MQTT being unavailable, slow, or rejecting the publish -
/// returns an SMTP failure so Postfix keeps the message queued and retries.
/// </summary>
public sealed class GatewayMessageStore(
    IAlertEventFactory eventFactory,
    IAlertPublisher publisher,
    ILogger<GatewayMessageStore> logger) : IMessageStore
{
    public async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        var raw = buffer.ToArray();

        var envelopeFrom = FormatMailbox(transaction.From);
        var envelopeRecipients = transaction.To.Select(FormatMailbox).ToList();

        MimeMessage mime;
        try
        {
            using var stream = new MemoryStream(raw, writable: false);
            mime = await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rejecting message from {From}: could not be parsed as MIME", envelopeFrom);
            return SmtpResponse.SyntaxError;
        }

        var alertEvent = eventFactory.Create(envelopeFrom, envelopeRecipients, raw, mime, DateTimeOffset.UtcNow);

        logger.LogInformation(
            "Received message {EventId} from {From} to {RecipientCount} recipient(s), subject={Subject}, size={Size} bytes",
            alertEvent.EventId, envelopeFrom, envelopeRecipients.Count, alertEvent.Headers.Subject, raw.Length);

        var published = await publisher.PublishAlertAsync(alertEvent, cancellationToken).ConfigureAwait(false);
        if (!published)
        {
            logger.LogWarning(
                "Temporary SMTP failure for {EventId}: MQTT did not confirm publication, Postfix will retry",
                alertEvent.EventId);
            return new SmtpResponse(SmtpReplyCode.Aborted, "requested action aborted: mqtt broker unavailable, try again later");
        }

        logger.LogInformation("Published {EventId} to MQTT, accepting SMTP transaction", alertEvent.EventId);
        return SmtpResponse.Ok;
    }

    private static string FormatMailbox(SmtpServer.Mail.IMailbox mailbox) => $"{mailbox.User}@{mailbox.Host}";
}
