using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.Video.Coordinators;
using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;
namespace Sufni.App.Video.ViewModels;

/// <summary>
/// Video pane of one open recorded session. It lives as long as the session
/// editor and derives the active clip from the session's clips and the
/// telemetry cursor. The view creates the native player while it is attached.
/// </summary>
public sealed partial class VideoPaneViewModel : ObservableObject, IDisposable
{
    private readonly IRecordedSessionTimeline timeline;
    private readonly IVideoCoordinator videoCoordinator;
    private readonly IDisposable clipsSubscription;
    private IReadOnlyList<SessionVideoSnapshot> clips = [];
    private long? sessionTimestamp;
    private double? telemetryDurationSeconds;

    [ObservableProperty] public partial bool HasVideo { get; private set; }
    [ObservableProperty] public partial string? VideoUri { get; private set; }

    public VideoPaneViewModel(
        Guid sessionId,
        IRecordedSessionTimeline timeline,
        ISessionVideoStore videoStore,
        IVideoCoordinator videoCoordinator,
        IVideoPlayerFactory playerFactory)
    {
        this.timeline = timeline;
        this.videoCoordinator = videoCoordinator;
        PlayerFactory = playerFactory;
        Playback = new VideoPlaybackViewModel(timeline);
        timeline.PropertyChanged += OnTimelinePropertyChanged;
        clipsSubscription = videoStore.Connect()
            .Filter(clip => clip.SessionId == sessionId)
            .ToCollection()
            .Subscribe(OnClipsChanged);
    }

    public VideoPlaybackViewModel Playback { get; }

    public IVideoPlayerFactory PlayerFactory { get; }

    /// <summary>
    /// The session start and the processed telemetry duration that map the
    /// telemetry cursor to video time.
    /// </summary>
    public void SetSessionTiming(long? timestamp, double? durationSeconds)
    {
        sessionTimestamp = timestamp;
        telemetryDurationSeconds = durationSeconds is > 0 ? durationSeconds : null;
        RefreshActiveClip();
    }

    /// <summary>
    /// Observes the presence of every linked file again.
    /// </summary>
    public Task RefreshClipsAsync() => videoCoordinator.RefreshAsync();

    /// <summary>
    /// Adds <paramref name="deltaSeconds"/> to the clip's alignment offset.
    /// Returns false when the clip no longer exists.
    /// </summary>
    public Task<bool> ShiftAlignmentAsync(Guid clipId, double deltaSeconds) =>
        videoCoordinator.ShiftAlignmentAsync(clipId, deltaSeconds);

    public SessionVideoSnapshot? ClipAt(double telemetrySeconds) =>
        SessionVideoSelection.Select(clips, sessionTimestamp, telemetrySeconds);

    public bool HasPresentClip(Guid clipId) =>
        clips.Any(clip => clip.Id == clipId && clip.FileExists);

    private void OnClipsChanged(IReadOnlyCollection<SessionVideoSnapshot> value)
    {
        clips = [.. value];
        HasVideo = clips.Any(clip => clip.FileExists);
        RefreshActiveClip();
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IRecordedSessionTimeline.NormalizedCursorPosition))
        {
            RefreshActiveClip();
        }
    }

    private void RefreshActiveClip()
    {
        if (ClipAt(CurrentTelemetrySeconds()) is not { } clip)
        {
            VideoUri = null;
            Playback.SetPlaybackContext(null);
            return;
        }

        VideoUri = new Uri(clip.Path).AbsoluteUri;
        Playback.SetPlaybackContext(new VideoPlaybackContext(
            SessionVideoSelection.SessionOffsetSeconds(clip, sessionTimestamp),
            clip.DurationSeconds,
            telemetryDurationSeconds,
            clip.AlignmentOffsetSeconds));
    }

    private double CurrentTelemetrySeconds() =>
        telemetryDurationSeconds is { } duration && timeline.NormalizedCursorPosition is { } cursor
            ? Math.Clamp(cursor, 0, 1) * duration
            : 0;

    public void Dispose()
    {
        clipsSubscription.Dispose();
        timeline.PropertyChanged -= OnTimelinePropertyChanged;
        Playback.Dispose();
    }
}
