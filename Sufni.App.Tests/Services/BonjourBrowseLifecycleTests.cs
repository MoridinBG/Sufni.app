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
        var resolutionId = lifecycle.TrackPending("result", "pending", canceled.Add);

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
        var firstResolutionId = lifecycle.TrackPending("result", "first", canceled.Add);
        var secondResolutionId = lifecycle.TrackPending("result", "second", canceled.Add);

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
        var resolutionId = lifecycle.TrackPending("pending", "pending-value", canceled.Add);
        var resolvedId = lifecycle.TrackPending("resolved", "resolved-value", canceled.Add);
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
        var resolutionId = lifecycle.TrackPending("result", "pending", _ => { });
        var expected = new ServiceAnnouncement(IPAddress.Parse("192.168.0.10"), 8080);

        Assert.False(lifecycle.TryRemoveResolved("result", out _));
        Assert.True(lifecycle.TryResolve("result", resolutionId, expected));
        Assert.True(lifecycle.TryRemoveResolved("result", out var announcement));
        Assert.Equal(expected.Address, announcement!.Address);
        Assert.Equal(expected.Port, announcement.Port);
        Assert.False(lifecycle.TryRemoveResolved("result", out _));
    }
}