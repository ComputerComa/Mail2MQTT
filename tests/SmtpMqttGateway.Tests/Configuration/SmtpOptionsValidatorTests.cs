using Microsoft.Extensions.Options;
using SmtpMqttGateway.Configuration;

namespace SmtpMqttGateway.Tests.Configuration;

public class SmtpOptionsValidatorTests
{
    private static SmtpOptions ValidOptions(string listenAddress) => new()
    {
        ListenAddress = listenAddress,
        Port = 10025,
        ServerName = "smtp-gateway.internal",
        MaxMessageBytes = 1_048_576,
        MaxTextLength = 65536,
        MaxHtmlLength = 131072,
        MaxAttachmentCount = 20,
    };

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.5.5.5")] // the whole 127.0.0.0/8 block is loopback
    [InlineData("::1")]
    public void Validate_LoopbackListenAddress_Succeeds(string listenAddress)
    {
        var result = new SmtpOptionsValidator().Validate(null, ValidOptions(listenAddress));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    [InlineData("::")]
    public void Validate_NonLoopbackListenAddress_Fails(string listenAddress)
    {
        var result = new SmtpOptionsValidator().Validate(null, ValidOptions(listenAddress));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("loopback"));
    }

    [Fact]
    public void Validate_InvalidIpAddress_Fails()
    {
        var result = new SmtpOptionsValidator().Validate(null, ValidOptions("not-an-ip"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("not a valid IP address"));
    }
}
