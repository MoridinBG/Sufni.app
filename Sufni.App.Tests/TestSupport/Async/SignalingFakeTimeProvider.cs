using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Sufni.App.Tests.TestSupport.Async;

/// <summary>Allows tests to advance virtual time after background work has scheduled a timer.</summary>
internal sealed class SignalingFakeTimeProvider : FakeTimeProvider
{
    private readonly Channel<TimeSpan> scheduledTimers = Channel.CreateUnbounded<TimeSpan>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        scheduledTimers.Writer.TryWrite(dueTime);
        return timer;
    }

    public async Task WaitForTimerAsync(TimeSpan dueTime, CancellationToken cancellationToken)
    {
        while (await scheduledTimers.Reader.ReadAsync(cancellationToken) != dueTime) { }
    }

    public async Task WaitForTimerAsync(CancellationToken cancellationToken) =>
        await scheduledTimers.Reader.ReadAsync(cancellationToken);
}
