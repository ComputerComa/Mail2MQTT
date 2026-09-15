using SmtpMqttGateway.Models;
using SmtpMqttGateway.Mqtt;

namespace SmtpMqttGateway.Tests.TestSupport;

public sealed class FakeAlertPublisher : IAlertPublisher
{
    private readonly Func<AlertEnvelopeV1, CancellationToken, Task<bool>> _behavior;

    public List<AlertEnvelopeV1> PublishedEvents { get; } = [];

    public FakeAlertPublisher(Func<AlertEnvelopeV1, CancellationToken, Task<bool>> behavior)
    {
        _behavior = behavior;
    }

    public static FakeAlertPublisher AlwaysSucceeds() => new((_, _) => Task.FromResult(true));

    public static FakeAlertPublisher AlwaysFails() => new((_, _) => Task.FromResult(false));

    public static FakeAlertPublisher TimesOut() => new(async (_, ct) =>
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Simulates a publish that never completes before the caller's timeout fires.
        }

        return false;
    });

    public async Task<bool> PublishAlertAsync(AlertEnvelopeV1 alertEvent, CancellationToken cancellationToken)
    {
        var result = await _behavior(alertEvent, cancellationToken);
        if (result)
        {
            PublishedEvents.Add(alertEvent);
        }

        return result;
    }
}
