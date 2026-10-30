using Sufni.App.Video.Stores;
using Sufni.App.Video.Models;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.ViewModels;

namespace Sufni.App.Tests.Video.ViewModels;

public class VideoPaneViewModelTests : IDisposable
{
    private const long SessionTimestamp = 1_000;

    private readonly VideoFixture fixture = new();
    private readonly TestRecordedSessionTimeline timeline = new();
    private readonly Guid sessionId = Guid.NewGuid();

    [Fact]
    public async Task HasVideo_IsFalse_WhenClipFileIsMissing()
    {
        var missing = fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 60, fileExists: false);

        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, Observable.Never<VideoSessionTiming>(), missing);

        Assert.False(pane.HasVideo);
        Assert.Null(pane.VideoUri);
    }

    [Fact]
    public async Task HasVideo_IgnoresClipsOfOtherSessions()
    {
        var other = fixture.CreateClip(Guid.NewGuid(), SessionTimestamp, durationSeconds: 60);

        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, Observable.Never<VideoSessionTiming>(), other);

        Assert.False(pane.HasVideo);
    }

    [Fact]
    public async Task HasVideo_BecomesTrue_WhenClipIsLinkedLater()
    {
        var store = await fixture.CreateStoreAsync();
        using var pane = new VideoPaneViewModel(sessionId, timeline, Observable.Never<VideoSessionTiming>(), store, fixture.Coordinator, fixture.PlayerFactory);

        await store.PublishClipsAsync([SessionVideoSnapshot.From(fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 60))]);

        Assert.True(pane.HasVideo);
    }

    [Fact]
    public async Task VideoUri_FollowsCursorToClipAtTelemetryTime()
    {
        var first = fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 20);
        var second = fixture.CreateClip(sessionId, SessionTimestamp + 20, durationSeconds: 20);
        using var pane = await fixture.CreatePaneAsync(
            sessionId,
            timeline,
            Observable.Return(new VideoSessionTiming(SessionTimestamp, DurationSeconds: 40)),
            first,
            second);

        timeline.SetCursorPosition(0.25);
        var firstUri = pane.VideoUri;
        timeline.SetCursorPosition(0.75);

        Assert.Equal(new Uri(first.Path).AbsoluteUri, firstUri);
        Assert.Equal(new Uri(second.Path).AbsoluteUri, pane.VideoUri);
    }

    [Fact]
    public async Task Playback_CannotSync_UntilTelemetryDurationIsKnown()
    {
        var clip = fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 60);
        using var timing = new Subject<VideoSessionTiming>();
        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, timing, clip);

        var canSyncBeforeTiming = pane.Playback.CanSync;
        timing.OnNext(new VideoSessionTiming(SessionTimestamp, DurationSeconds: 30));

        Assert.False(canSyncBeforeTiming);
        Assert.True(pane.Playback.CanSync);
    }

    [Fact]
    public async Task TimelinePlayback_WithSync_DoesNotSeekWhileTheCursorFollowsTheVideo()
    {
        var clip = fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 60);
        using var pane = await fixture.CreatePaneAsync(
            sessionId,
            timeline,
            Observable.Return(new VideoSessionTiming(SessionTimestamp, DurationSeconds: 30)),
            clip);
        var backend = new FakeVideoPlaybackBackend();
        timeline.SetCursorPosition(0);
        pane.Playback.SetBackend(backend);
        pane.Playback.IsSyncEnabled = true;
        timeline.RequestPlaybackToggle();
        backend.SeekedPositions.Clear();

        backend.RaisePositionChanged(TimeSpan.FromSeconds(10));
        backend.RaisePositionChanged(TimeSpan.FromSeconds(11));

        Assert.Empty(backend.SeekedPositions);
    }

    public void Dispose() => fixture.Dispose();
}
