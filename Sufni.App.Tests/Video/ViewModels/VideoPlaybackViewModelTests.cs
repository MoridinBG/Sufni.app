using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Models;
using Sufni.App.Shared.Models;
using Sufni.App.Video.ViewModels;
using static Sufni.App.Tests.Video.ViewModels.VideoPlaybackHarness;

namespace Sufni.App.Tests.Video.ViewModels;

public class VideoPlaybackViewModelTests
{
    [Fact]
    public void PlaybackFailure_ReleasesTimelineAndDisablesPlayback_UntilSuccessfulReload()
    {
        using var harness = new VideoPlaybackHarness(Context(), cursor: 0.25);
        using var viewModel = harness.ViewModel;
        viewModel.IsSyncEnabled = true;
        harness.Timeline.RequestPlaybackToggle();

        viewModel.ReportFailure(new(VideoPlaybackFailureReason.Playback, "native diagnostic"));
        viewModel.CompleteLoad(harness.Backend);
        harness.Timeline.RequestPlaybackToggle();

        Assert.True(viewModel.HasFailure);
        Assert.False(viewModel.PlayPauseCommand.CanExecute(null));
        Assert.False(viewModel.ToggleSyncCommand.CanExecute(null));
        Assert.False(harness.Timeline.IsPlaybackActive);
        Assert.False(harness.Backend.IsPlaying);
        Assert.True(viewModel.Failure!.HasDetails);

        viewModel.BeginLoad(true);
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.PlayPauseCommand.CanExecute(null));
        viewModel.CompleteLoad(harness.Backend);

        Assert.False(viewModel.HasFailure);
        Assert.True(viewModel.PlayPauseCommand.CanExecute(null));
        Assert.True(viewModel.IsSyncEnabled);
        Assert.True(viewModel.CanSync);
    }

    [Fact]
    public void Sync_SeeksBackendToAlignedVideoTime_WhenTimelineCursorChanges()
    {
        using var harness = new VideoPlaybackHarness(Context(
            sessionOffsetSeconds: 2,
            videoDurationSeconds: 10,
            alignmentOffsetSeconds: 1));

        harness.ViewModel.ToggleSyncCommand.Execute(null);
        harness.Timeline.SetCursorPosition(0.25);

        Assert.Equal(TimeSpan.FromSeconds(8), harness.Backend.SeekedPositions.Single());
        harness.ViewModel.ToggleSyncCommand.Execute(null);
        harness.Timeline.SetCursorPosition(0.5);
        Assert.Single(harness.Backend.SeekedPositions);
    }

    [Fact]
    public void Sync_ClampsSeekToVideoDuration()
    {
        using var harness = new VideoPlaybackHarness(Context(
            sessionOffsetSeconds: 8,
            videoDurationSeconds: 10,
            alignmentOffsetSeconds: 5));

        harness.ViewModel.IsSyncEnabled = true;
        harness.Timeline.SetCursorPosition(0.5);

        Assert.Equal(TimeSpan.FromSeconds(10), harness.Backend.SeekedPositions.Single());
    }

    [Fact]
    public void PlayPause_PreservesSyncAndSuppressesSeekWhilePlaying()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30));
        harness.ViewModel.IsSyncEnabled = true;
        harness.Backend.SeekedPositions.Clear();

        harness.ViewModel.PlayPauseCommand.Execute(null);
        harness.Timeline.SetCursorPosition(0.5);

        Assert.True(harness.Backend.IsPlaying);
        Assert.True(harness.ViewModel.IsSyncEnabled);
        Assert.True(harness.ViewModel.IsPlaying);
        Assert.Empty(harness.Backend.SeekedPositions);

        harness.ViewModel.PlayPauseCommand.Execute(null);
        harness.Backend.SeekedPositions.Clear();
        harness.Timeline.SetCursorPosition(0.75);

        Assert.False(harness.Backend.IsPlaying);
        Assert.True(harness.ViewModel.IsSyncEnabled);
        Assert.False(harness.ViewModel.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(harness.Backend.SeekedPositions));
    }

    [Fact]
    public void SetPlaybackContext_DisablesSync_WhenTelemetryDurationIsMissing()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 10));
        harness.ViewModel.IsSyncEnabled = true;

        harness.ViewModel.SetPlaybackContext(Context(videoDurationSeconds: 10, telemetryDurationSeconds: null));

        Assert.False(harness.ViewModel.CanSync);
        Assert.False(harness.ViewModel.IsSyncEnabled);
    }

    [Fact]
    public void SetPlaybackContext_SeeksToCursorWithUpdatedAlignment_WhenSyncIsEnabled()
    {
        using var harness = new VideoPlaybackHarness(
            Context(sessionOffsetSeconds: 2, videoDurationSeconds: 20, alignmentOffsetSeconds: 1),
            cursor: 0.25);
        harness.ViewModel.IsSyncEnabled = true;
        harness.Backend.SeekedPositions.Clear();

        harness.ViewModel.SetPlaybackContext(Context(
            sessionOffsetSeconds: 2,
            videoDurationSeconds: 20,
            alignmentOffsetSeconds: 4));

        Assert.True(harness.ViewModel.IsSyncEnabled);
        Assert.Equal(TimeSpan.FromSeconds(11), Assert.Single(harness.Backend.SeekedPositions));
    }

    [Fact]
    public void TimelinePlaybackToggle_StartsPlaybackFromCursor_AndPreservesSync()
    {
        using var harness = new VideoPlaybackHarness(
            Context(sessionOffsetSeconds: 2, videoDurationSeconds: 10, alignmentOffsetSeconds: 1),
            cursor: 0.25);
        harness.ViewModel.IsSyncEnabled = true;
        harness.Backend.SeekedPositions.Clear();

        harness.Timeline.RequestPlaybackToggle();

        Assert.True(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.True(harness.Timeline.IsPlaybackActive);
        Assert.True(harness.Backend.IsPlaying);
        Assert.True(harness.ViewModel.IsSyncEnabled);
        Assert.Equal(TimeSpan.FromSeconds(8), Assert.Single(harness.Backend.SeekedPositions));
    }

    [Fact]
    public void TimelinePlaybackToggle_ResumesSyncAfterPlaybackPauses()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.ViewModel.IsSyncEnabled = true;
        harness.Timeline.RequestPlaybackToggle();
        harness.Backend.SeekedPositions.Clear();

        harness.Backend.RaisePositionChanged(TimeSpan.FromSeconds(10));
        harness.Timeline.RequestPlaybackToggle();
        harness.Timeline.SetCursorPosition(0.75);

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.False(harness.Backend.IsPlaying);
        Assert.True(harness.ViewModel.IsSyncEnabled);
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(harness.Backend.SeekedPositions));
    }

    [Fact]
    public void TimelinePlaybackToggle_SecondToggle_PausesPlayback()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.Timeline.RequestPlaybackToggle();

        harness.Timeline.RequestPlaybackToggle();

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.False(harness.Backend.IsPlaying);
    }

    [Fact]
    public void TimelinePlaybackToggle_IsIgnored_WhenTelemetryDurationIsMissing()
    {
        using var harness = new VideoPlaybackHarness(
            Context(videoDurationSeconds: 30, telemetryDurationSeconds: null),
            cursor: 0.25);

        harness.Timeline.RequestPlaybackToggle();

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.False(harness.Backend.IsPlaying);
    }

    [Fact]
    public void TimelinePlayback_MovesTimelineCursor_AsVideoPositionAdvances()
    {
        using var harness = new VideoPlaybackHarness(
            Context(sessionOffsetSeconds: 2, videoDurationSeconds: 30, alignmentOffsetSeconds: 1),
            cursor: 0);
        harness.Timeline.RequestPlaybackToggle();
        harness.Backend.SeekedPositions.Clear();

        harness.Backend.RaisePositionChanged(TimeSpan.FromSeconds(8));

        Assert.Equal(0.25, harness.Timeline.NormalizedCursorPosition);
        harness.Backend.RaisePositionChanged(TimeSpan.FromSeconds(10));
        Assert.Equal(0.35, harness.Timeline.NormalizedCursorPosition);
        Assert.Empty(harness.Backend.SeekedPositions);
    }

    [Fact]
    public void TimelinePlayback_ClampsCursor_WhenVideoRunsPastTelemetryEnd()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 60), cursor: 0.9);
        harness.Timeline.RequestPlaybackToggle();

        harness.Backend.RaisePositionChanged(TimeSpan.FromSeconds(45));

        Assert.Equal(1, harness.Timeline.NormalizedCursorPosition);
    }

    [Fact]
    public void TimelinePlayback_DoesNotMoveCursor_WhenInactive()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.5);

        harness.Backend.RaisePositionChanged(TimeSpan.FromSeconds(10));

        Assert.Equal(0.5, harness.Timeline.NormalizedCursorPosition);
    }

    [Fact]
    public void TimelinePlaybackStopRequest_PausesActivePlayback()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.Timeline.RequestPlaybackToggle();

        harness.Timeline.RequestPlaybackStop();

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.False(harness.Timeline.IsPlaybackActive);
        Assert.False(harness.Backend.IsPlaying);
    }

    [Fact]
    public void TimelinePlaybackStopRequest_DoesNotStartPlayback_WhenInactive()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);

        harness.Timeline.RequestPlaybackStop();

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.False(harness.Backend.IsPlaying);
    }

    [Fact]
    public void TimelinePlayback_Deactivates_WhenBackendStopsOnItsOwn()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.Timeline.RequestPlaybackToggle();

        harness.Backend.SimulateEndReached();

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
        Assert.False(harness.ViewModel.IsPlaying);
    }

    [Fact]
    public void TimelinePlayback_Deactivates_WhenTelemetryDurationDisappears()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.Timeline.RequestPlaybackToggle();

        harness.ViewModel.SetPlaybackContext(Context(videoDurationSeconds: 30, telemetryDurationSeconds: null));

        Assert.False(harness.ViewModel.IsTimelinePlaybackActive);
    }

    [Fact]
    public void Dispose_ClearsPlaybackDrivenFlagOnTimeline_WhileActive()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.Timeline.RequestPlaybackToggle();

        harness.ViewModel.Dispose();

        Assert.False(harness.Timeline.IsPlaybackActive);
    }

    [Fact]
    public void SetBackend_Null_ReleasesTimelinePlayback()
    {
        using var harness = new VideoPlaybackHarness(Context(videoDurationSeconds: 30), cursor: 0.25);
        harness.Timeline.RequestPlaybackToggle();

        harness.ViewModel.SetBackend(null);

        Assert.False(harness.Timeline.IsPlaybackActive);
        Assert.False(harness.ViewModel.PlayPauseCommand.CanExecute(null));
    }
}

internal sealed class VideoPlaybackHarness : IDisposable
{
    public VideoPlaybackHarness(
        VideoPlaybackContext? context = null,
        double? cursor = null)
    {
        if (cursor is not null)
        {
            Timeline.SetCursorPosition(cursor);
        }

        ViewModel = new VideoPlaybackViewModel(Timeline);
        ViewModel.SetBackend(Backend);

        if (context is not null)
        {
            ViewModel.SetPlaybackContext(context);
        }
    }

    public void Dispose() => ViewModel.Dispose();

    public FakeVideoPlaybackBackend Backend { get; } = new();
    public TestRecordedSessionTimeline Timeline { get; } = new();
    public VideoPlaybackViewModel ViewModel { get; }

    public static VideoPlaybackContext Context(
        double sessionOffsetSeconds = 0,
        double videoDurationSeconds = 30,
        double? telemetryDurationSeconds = 20,
        double alignmentOffsetSeconds = 0) =>
        new(
            SessionOffsetSeconds: sessionOffsetSeconds,
            VideoDurationSeconds: videoDurationSeconds,
            TelemetryDurationSeconds: telemetryDurationSeconds,
            AlignmentOffsetSeconds: alignmentOffsetSeconds);
}
