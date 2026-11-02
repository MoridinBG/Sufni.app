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

    [Fact]
    public async Task SourceArrival_ExposesSharedClipAtSplitWindowTime_WithoutRewritingAlignment()
    {
        var clip = fixture.CreateClip("source-hash", 995, 60, alignmentOffsetSeconds: 5);
        using var timing = new BehaviorSubject<VideoSessionTiming>(new(null, 10.25, 5));
        using var pane = await fixture.CreatePaneAsync(timeline, timing, clip);
        Assert.False(pane.HasVideo);
        timing.OnNext(new VideoSessionTiming("source-hash", 10.25, 5));
        var backend = new FakeVideoPlaybackBackend();
        pane.Playback.SetBackend(backend);
        pane.Playback.IsSyncEnabled = true;

        timeline.SetCursorPosition(0.4);

        Assert.True(pane.HasVideo);
        Assert.Equal(TimeSpan.FromSeconds(17.25), backend.SeekedPositions.Last());
        Assert.Equal(5, clip.AlignmentOffsetSeconds);
        timing.OnNext(new VideoSessionTiming("source-hash", 70, 5));
        Assert.False(pane.HasVideo);
        Assert.Null(pane.VideoUri);
    }

    [Fact]
    public async Task HasVideo_IsFalse_WhenClipFileIsMissing()
    {
        var missing = fixture.CreateClip("source-hash", SessionTimestamp, durationSeconds: 60, fileExists: false);

        using var pane = await fixture.CreatePaneAsync(timeline, Observable.Return(new VideoSessionTiming("source-hash", 0, 60)), missing);

        Assert.False(pane.HasVideo);
        Assert.Null(pane.VideoUri);
    }

    [Fact]
    public async Task HasVideo_IgnoresClipsOfOtherSources()
    {
        var other = fixture.CreateClip("other-source", SessionTimestamp, durationSeconds: 60);

        using var pane = await fixture.CreatePaneAsync(timeline, Observable.Return(new VideoSessionTiming("source-hash", 0, 60)), other);

        Assert.False(pane.HasVideo);
    }

    [Fact]
    public async Task HasVideo_BecomesTrue_WhenClipIsLinkedLater()
    {
        var store = await fixture.CreateStoreAsync();
        using var pane = new VideoPaneViewModel(timeline, Observable.Return(new VideoSessionTiming("source-hash", 0, 60)), store, fixture.Coordinator, fixture.PlayerFactory);

        await store.PublishClipsAsync([fixture.CreateClip("source-hash", SessionTimestamp, durationSeconds: 60)]);

        Assert.True(pane.HasVideo);
    }

    [Fact]
    public async Task VideoUri_FollowsCursorToClipAtTelemetryTime()
    {
        var first = fixture.CreateClip("source-hash", SessionTimestamp, durationSeconds: 20);
        var second = fixture.CreateClip("source-hash", SessionTimestamp + 20, durationSeconds: 20, alignmentOffsetSeconds: -20);
        using var pane = await fixture.CreatePaneAsync(
            timeline,
            Observable.Return(new VideoSessionTiming("source-hash", 0, DurationSeconds: 40)),
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
        var clip = fixture.CreateClip("source-hash", SessionTimestamp, durationSeconds: 60);
        using var timing = new Subject<VideoSessionTiming>();
        using var pane = await fixture.CreatePaneAsync(timeline, timing, clip);

        var canSyncBeforeTiming = pane.Playback.CanSync;
        timing.OnNext(new VideoSessionTiming("source-hash", 0, DurationSeconds: 30));

        Assert.False(canSyncBeforeTiming);
        Assert.True(pane.Playback.CanSync);
    }

    [Fact]
    public async Task TimelinePlayback_WithSync_DoesNotSeekWhileTheCursorFollowsTheVideo()
    {
        var clip = fixture.CreateClip("source-hash", SessionTimestamp, durationSeconds: 60);
        using var pane = await fixture.CreatePaneAsync(
            timeline,
            Observable.Return(new VideoSessionTiming("source-hash", 0, DurationSeconds: 30)),
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
