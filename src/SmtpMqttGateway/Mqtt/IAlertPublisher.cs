using SmtpMqttGateway.Models;

namespace SmtpMqttGateway.Mqtt;

public interface IAlertPublisher
{
    /// <summary>
    /// Publishes the alert event at QoS 1 and waits for broker acknowledgement.
    /// Returns false whenever the SMTP transaction must be temporarily failed
    /// (broker disconnected, publish timed out, or the broker rejected it).
    /// </summary>
    Task<bool> PublishAlertAsync(AlertEnvelopeV1 alertEvent, CancellationToken cancellationToken);
}
