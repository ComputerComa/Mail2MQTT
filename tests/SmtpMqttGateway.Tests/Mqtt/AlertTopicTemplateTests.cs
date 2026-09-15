using SmtpMqttGateway.Models;
using SmtpMqttGateway.Mqtt;

namespace SmtpMqttGateway.Tests.Mqtt;

public class AlertTopicTemplateTests
{
    private static AlertEnvelopeV1 BuildEvent(string from, params string[] recipients) => new()
    {
        EventId = "irrelevant",
        ReceivedAtUtc = DateTimeOffset.UtcNow,
        Envelope = new EnvelopeInfo { From = from, Recipients = recipients },
        Headers = new HeaderInfo(),
        Content = new ContentInfo(),
    };

    [Fact]
    public void Resolve_SenderLocal_ExtractsLocalPartOnly()
    {
        var evt = BuildEvent("root@truenas.local", "ops@example.com");

        var topic = AlertTopicTemplate.Resolve("homelab/alerts/{senderLocal}", evt);

        Assert.Equal("homelab/alerts/root", topic);
    }

    [Fact]
    public void Resolve_SupportsMultipleVariablesAndLiteralSegments()
    {
        var evt = BuildEvent("root@truenas.local", "ops@example.com");

        var topic = AlertTopicTemplate.Resolve("homelab/senders/{senderDomain}/{senderLocal}/pve", evt);

        Assert.Equal("homelab/senders/truenas.local/root/pve", topic);
    }

    [Fact]
    public void Resolve_RecipientVariables_UseFirstRecipient()
    {
        var evt = BuildEvent("root@truenas.local", "alerts@notify.home.arpa", "second@example.com");

        var topic = AlertTopicTemplate.Resolve("homelab/{recipientLocal}/{recipientDomain}", evt);

        Assert.Equal("homelab/alerts/notify.home.arpa", topic);
    }

    [Fact]
    public void Resolve_PlusAddressing_IsSanitized()
    {
        var evt = BuildEvent("alerts+truenas@example.com");

        var topic = AlertTopicTemplate.Resolve("homelab/alerts/{senderLocal}", evt);

        Assert.DoesNotContain('+', topic);
        Assert.Equal("homelab/alerts/alerts_truenas", topic);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_LowercasesSubstitutions()
    {
        var evt = BuildEvent("Root@TrueNAS.Local");

        var topic = AlertTopicTemplate.Resolve("homelab/alerts/{senderLocal}", evt);

        Assert.Equal("homelab/alerts/root", topic);
    }

    [Fact]
    public void Resolve_MissingRecipient_FallsBackToUnknown()
    {
        var evt = BuildEvent("root@truenas.local"); // no recipients

        var topic = AlertTopicTemplate.Resolve("homelab/{recipientLocal}", evt);

        Assert.Equal("homelab/unknown", topic);
    }

    [Fact]
    public void Validate_UnknownVariable_ReturnsError()
    {
        var errors = AlertTopicTemplate.Validate("homelab/alerts/{bogus}");

        Assert.Contains(errors, e => e.Contains("bogus"));
    }

    [Fact]
    public void Validate_LiteralWildcardCharacters_ReturnsError()
    {
        var errors = AlertTopicTemplate.Validate("homelab/alerts/+{senderLocal}");

        Assert.Contains(errors, e => e.Contains('+'));
    }

    [Fact]
    public void Validate_EmptyTemplate_ReturnsError()
    {
        var errors = AlertTopicTemplate.Validate("   ");

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_KnownVariablesOnly_ReturnsNoErrors()
    {
        var errors = AlertTopicTemplate.Validate("homelab/alerts/{senderDomain}/{senderLocal}");

        Assert.Empty(errors);
    }
}
