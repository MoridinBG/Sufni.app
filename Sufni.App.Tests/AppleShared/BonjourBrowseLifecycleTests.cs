using System.Net;

using Sufni.App.Infrastructure;
using Sufni.App.AppleShared;
namespace Sufni.App.Tests.AppleShared;

public class BonjourBrowseLifecycleTests
{
    [Fact]
    public void IncrementalResolution_PublishesCandidatesUntilCompleted_AndRetainsPresence()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        lifecycle.TryStart();
        lifecycle.TryTrackPending("daq", "resolver", _ => { }, out var id);
        var updates = new List<ServiceAnnouncement>();
        var first = new ServiceAnnouncement(IPAddress.Loopback, 1557);
        var complete = new ServiceAnnouncement(new ServiceEndpoint([IPAddress.Loopback, IPAddress.IPv6Loopback], 1557), null, null);

        Assert.True(lifecycle.PublishResolved("daq", id, first, updates.Add));
        Assert.True(lifecycle.PublishResolved("daq", id, complete, updates.Add));
        var canceled = new List<string>();
        Assert.True(lifecycle.CompletePending("daq", id, canceled.Add));
        Assert.Equal(["resolver"], canceled);
        Assert.False(lifecycle.PublishResolved("daq", id, first, updates.Add));
        Assert.Equal(2, updates.Count);
        Assert.True(lifecycle.TryRemoveResolved("daq", out var removed));
        Assert.Same(complete, removed);
    }

    [Fact]
    public void StopDuringIncrementalResolution_RemovesPresenceAndSuppressesLateCallbacks()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();
        lifecycle.TryStart();
        lifecycle.TryTrackPending("daq", "resolver", _ => { }, out var id);
        var announcement = new ServiceAnnouncement(IPAddress.Loopback, 1557);
        lifecycle.PublishResolved("daq", id, announcement, _ => { });
        var removed = new List<ServiceAnnouncement>();
        var canceled = new List<string>();

        lifecycle.TryStop(canceled.Add, removed.Add);

        Assert.Same(announcement, Assert.Single(removed));
        Assert.Equal(["resolver"], canceled);
        Assert.False(lifecycle.PublishResolved("daq", id, announcement, _ => Assert.Fail("Late presence")));
        lifecycle.TryStart();
        lifecycle.TryTrackPending("daq", "replacement", canceled.Add, out var replacementId);
        Assert.False(lifecycle.CompletePending("daq", id, canceled.Add));
        Assert.True(lifecycle.PublishResolved("daq", replacementId, announcement, _ => { }));
        Assert.Single(canceled);
    }

    [Fact]
    public void TryStart_AndTryStop_AreRepeatableAcrossBrowseSessions()
    {
        var lifecycle = new BonjourBrowseLifecycle<string>();

        Assert.True(lifecycle.TryStart());
        Assert.False(lifecycle.TryStart());
        Assert.True(lifecycle.TryStop(_ => { }, _ => { }));
        Assert.False(lifecycle.TryStop(_ => { }, _ => { }));
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

        var removed = new List<ServiceAnnouncement>();
        Assert.True(lifecycle.TryStop(canceled.Add, removed.Add));
        Assert.Equal((ushort)4567, Assert.Single(removed).Port);
        Assert.False(lifecycle.TryStop(canceled.Add, removed.Add));
        Assert.Single(removed);
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
        }, _ => { }));

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