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

        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, missing);

        Assert.False(pane.HasVideo);
        Assert.Null(pane.VideoUri);
    }

    [Fact]
    public async Task HasVideo_IgnoresClipsOfOtherSessions()
    {
        var other = fixture.CreateClip(Guid.NewGuid(), SessionTimestamp, durationSeconds: 60);

        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, other);

        Assert.False(pane.HasVideo);
    }

    [Fact]
    public async Task HasVideo_BecomesTrue_WhenClipIsLinkedLater()
    {
        var store = await fixture.CreateStoreAsync();
        using var pane = new VideoPaneViewModel(sessionId, timeline, store, fixture.Coordinator, fixture.PlayerFactory);

        await store.PublishClipsAsync([fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 60)]);

        Assert.True(pane.HasVideo);
    }

    [Fact]
    public async Task VideoUri_FollowsCursorToClipAtTelemetryTime()
    {
        var first = fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 20);
        var second = fixture.CreateClip(sessionId, SessionTimestamp + 20, durationSeconds: 20);
        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, first, second);
        pane.SetSessionTiming(SessionTimestamp, durationSeconds: 40);

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
        using var pane = await fixture.CreatePaneAsync(sessionId, timeline, clip);

        var canSyncBeforeTiming = pane.Playback.CanSync;
        pane.SetSessionTiming(SessionTimestamp, durationSeconds: 30);

        Assert.False(canSyncBeforeTiming);
        Assert.True(pane.Playback.CanSync);
    }

    public void Dispose() => fixture.Dispose();
}
