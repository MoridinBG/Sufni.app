using System.Net;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class BonjourBrowseLifecycleTests
{
    [Fact]
    public void TryStart_AndTryStop_AreRepeatableAcrossBrowseSessions()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();

        Assert.True(lifecycle.TryStart());
        Assert.False(lifecycle.TryStart());
        Assert.True(lifecycle.TryStop(_ => { }));
        Assert.False(lifecycle.TryStop(_ => { }));
        Assert.True(lifecycle.TryStart());
    }

    [Fact]
    public void CancelPending_SuppressesLateResolve()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        lifecycle.TryStart();
        var canceled = new List<string>();
        Assert.True(lifecycle.TryTrackPending("result", "pending", canceled.Add, out var resolutionId));

        Assert.True(lifecycle.CancelPending("result", canceled.Add));
        Assert.Equal(["pending"], canceled);
        Assert.False(lifecycle.TryResolve("result", resolutionId, new ServiceAnnouncement(IPAddress.Loopback, 1234)));
    }

    [Fact]
    public void TrackPending_ReplacesExistingPendingResolutionForSameKey()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        lifecycle.TryStart();
        var canceled = new List<string>();
        Assert.True(lifecycle.TryTrackPending("result", "first", canceled.Add, out var firstResolutionId));
        Assert.True(lifecycle.TryTrackPending("result", "second", canceled.Add, out var secondResolutionId));

        Assert.Equal(["first"], canceled);
        Assert.False(lifecycle.TryResolve("result", firstResolutionId, new ServiceAnnouncement(IPAddress.Loopback, 1234)));
        Assert.True(lifecycle.TryResolve("result", secondResolutionId, new ServiceAnnouncement(IPAddress.Loopback, 2345)));
        Assert.True(lifecycle.TryRemoveResolved("result", out var announcement));
        Assert.Equal((ushort)2345, announcement!.Port);
    }

    [Fact]
    public void TryStop_CancelsPendingAndClearsResolvedAnnouncements()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        lifecycle.TryStart();
        var canceled = new List<string>();
        Assert.True(lifecycle.TryTrackPending("pending", "pending-value", canceled.Add, out var resolutionId));
        Assert.True(lifecycle.TryTrackPending("resolved", "resolved-value", canceled.Add, out var resolvedId));
        Assert.True(lifecycle.TryResolve("resolved", resolvedId, new ServiceAnnouncement(IPAddress.Loopback, 4567)));

        Assert.True(lifecycle.TryStop(canceled.Add));
        Assert.Equal(["pending-value"], canceled);
        Assert.False(lifecycle.TryResolve("pending", resolutionId, new ServiceAnnouncement(IPAddress.Loopback, 1234)));
        Assert.False(lifecycle.TryRemoveResolved("resolved", out _));
    }

    [Fact]
    public void TryRemoveResolved_ReturnsAnnouncementOnlyAfterResolution()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        lifecycle.TryStart();
        Assert.True(lifecycle.TryTrackPending("result", "pending", _ => { }, out var resolutionId));
        var expected = new ServiceAnnouncement(IPAddress.Parse("192.168.0.10"), 8080);

        Assert.False(lifecycle.TryRemoveResolved("result", out _));
        Assert.True(lifecycle.TryResolve("result", resolutionId, expected));
        Assert.True(lifecycle.TryRemoveResolved("result", out var announcement));
        Assert.Equal(expected.Address, announcement!.Address);
        Assert.Equal(expected.Port, announcement.Port);
        Assert.False(lifecycle.TryRemoveResolved("result", out _));
    }

    [Fact]
    public void TryTrackPending_ReturnsFalse_WhenBrowsingIsInactive()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        var canceled = new List<string>();

        Assert.False(lifecycle.TryTrackPending("result", "pending", canceled.Add, out var resolutionId));
        Assert.Equal(0, resolutionId);
        Assert.Equal(["pending"], canceled);
        Assert.False(lifecycle.CancelPending("result", _ => { }));
    }

    [Fact]
    public async Task TryStop_RejectsLateTracks_WhileCancellationCallbacksAreRunning()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        Assert.True(lifecycle.TryStart());
        for (var index = 0; index < 8; index++)
        {
            Assert.True(lifecycle.TryTrackPending($"existing-{index}", $"existing-{index}", _ => { }, out _));
        }

        var firstCancellationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStopToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCount = 0;

        var stopTask = Task.Run(() => lifecycle.TryStop(value =>
        {
            if (Interlocked.Increment(ref cancellationCount) != 1)
            {
                return;
            }

            firstCancellationStarted.TrySetResult();
            allowStopToFinish.Task.GetAwaiter().GetResult();
        }));

        await firstCancellationStarted.Task;

        var lateTrackResults = new List<bool>();
        for (var index = 0; index < 8; index++)
        {
            lateTrackResults.Add(lifecycle.TryTrackPending($"late-{index}", $"late-{index}", _ => { }, out _));
        }

        allowStopToFinish.TrySetResult();

        Assert.True(await stopTask);
        Assert.All(lateTrackResults, Assert.False);
        Assert.False(lifecycle.CancelPending("late-0", _ => { }));
    }
}