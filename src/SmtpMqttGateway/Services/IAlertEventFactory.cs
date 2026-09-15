using MimeKit;
using SmtpMqttGateway.Models;

namespace SmtpMqttGateway.Services;

public interface IAlertEventFactory
{
    AlertEnvelopeV1 Create(
        string envelopeFrom,
        IReadOnlyList<string> envelopeRecipients,
        ReadOnlyMemory<byte> rawMessage,
        MimeMessage message,
        DateTimeOffset receivedAtUtc);
}
