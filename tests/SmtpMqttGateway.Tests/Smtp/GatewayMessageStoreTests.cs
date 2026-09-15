using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpMqttGateway.Configuration;
using SmtpMqttGateway.Services;
using SmtpMqttGateway.Smtp;
using SmtpMqttGateway.Tests.TestSupport;
using SmtpServer.Mail;
using SmtpServer.Protocol;

namespace SmtpMqttGateway.Tests.Smtp;

public class GatewayMessageStoreTests
{
    private static byte[] BuildValidMessage()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alerter", "alerter@example.com"));
        message.To.Add(new MailboxAddress("Ops", "ops@example.com"));
        message.Subject = "Test alert";
        message.Body = new TextPart("plain") { Text = "something happened" };

        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.ToArray();
    }

    private static FakeMessageTransaction BuildTransaction() => new()
    {
        From = new Mailbox("alerter", "example.com"),
        To = [new Mailbox("ops", "example.com")],
    };

    private static GatewayMessageStore CreateStore(FakeAlertPublisher publisher) =>
        new(
            new AlertEventFactory(Options.Create(new SmtpOptions())),
            publisher,
            NullLogger<GatewayMessageStore>.Instance);

    [Fact]
    public async Task SuccessfulPublish_ReturnsOk()
    {
        var publisher = FakeAlertPublisher.AlwaysSucceeds();
        var store = CreateStore(publisher);

        var response = await store.SaveAsync(
            null!,
            BuildTransaction(),
            new ReadOnlySequence<byte>(BuildValidMessage()),
            CancellationToken.None);

        Assert.Equal(SmtpResponse.Ok.ReplyCode, response.ReplyCode);
        Assert.Single(publisher.PublishedEvents);
    }

    [Fact]
    public async Task MqttUnavailable_ReturnsTemporaryFailure()
    {
        var publisher = FakeAlertPublisher.AlwaysFails();
        var store = CreateStore(publisher);

        var response = await store.SaveAsync(
            null!,
            BuildTransaction(),
            new ReadOnlySequence<byte>(BuildValidMessage()),
            CancellationToken.None);

        Assert.Equal(SmtpReplyCode.Aborted, response.ReplyCode);
        Assert.Equal(451, (int)response.ReplyCode);
        Assert.Empty(publisher.PublishedEvents);
    }

    [Fact]
    public async Task MqttTimeout_ReturnsTemporaryFailure()
    {
        var publisher = FakeAlertPublisher.TimesOut();
        var store = CreateStore(publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var response = await store.SaveAsync(
            null!,
            BuildTransaction(),
            new ReadOnlySequence<byte>(BuildValidMessage()),
            cts.Token);

        Assert.Equal(SmtpReplyCode.Aborted, response.ReplyCode);
        Assert.Empty(publisher.PublishedEvents);
    }
}
