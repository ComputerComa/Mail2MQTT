using SmtpServer;
using SmtpServer.Mail;

namespace SmtpMqttGateway.Tests.TestSupport;

public sealed class FakeMessageTransaction : IMessageTransaction
{
    public IMailbox From { get; set; } = new Mailbox("anonymous", "localhost");

    public IList<IMailbox> To { get; set; } = new List<IMailbox>();

    public IReadOnlyDictionary<string, string> Parameters { get; set; } =
        new Dictionary<string, string>();
}
