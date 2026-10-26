using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.Video.Models;
namespace Sufni.App.Video.ViewModels;

/// <summary>
/// Play, sync, and timeline-playback state of one open session. It lives as
/// long as the session editor; the native backend is attached only while a
/// video pane shows it. While timeline playback is active the video position
/// drives the telemetry cursor.
/// </summary>
public sealed partial class VideoPlaybackViewModel : ObservableObject, IDisposable
{
    private readonly IRecordedSessionTimeline timeline;
    private IVideoPlaybackBackend? backend;
    private VideoPlaybackContext? playbackContext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial bool IsSyncEnabled { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleSyncCommand))]
    public partial bool CanSync { get; set; }
    [ObservableProperty] public partial bool IsTimelinePlaybackActive { get; set; }
    [ObservableProperty] public partial double VideoWidth { get; set; } = 16;
    [ObservableProperty] public partial double VideoHeight { get; set; } = 9;

    public VideoPlaybackViewModel(IRecordedSessionTimeline timeline)
    {
        this.timeline = timeline;
        timeline.PropertyChanged += OnTimelinePropertyChanged;
        timeline.PlaybackToggleRequested += OnTimelinePlaybackToggleRequested;
        timeline.PlaybackStopRequested += OnTimelinePlaybackStopRequested;
    }

    public bool IsPaused => !IsPlaying;

    /// <summary>
    /// Attaches the backend of a newly created native player, already loaded
    /// with the active clip, and seeks it to the cursor. Passing null releases
    /// cursor control.
    /// </summary>
    public void SetBackend(IVideoPlaybackBackend? value)
    {
        if (backend is not null)
        {
            backend.PlaybackStateChanged -= OnBackendPlaybackStateChanged;
            backend.PositionChanged -= OnBackendPositionChanged;
        }

        backend = value;
        IsTimelinePlaybackActive = false;

        if (backend is not null)
        {
            backend.PlaybackStateChanged += OnBackendPlaybackStateChanged;
            backend.PositionChanged += OnBackendPositionChanged;
            SeekToCursorIfPresent();
        }

        RefreshPlaybackState();
        PlayPauseCommand.NotifyCanExecuteChanged();
    }

    public void SetPlaybackContext(VideoPlaybackContext? value)
    {
        playbackContext = value;
        CanSync = value?.CanSync == true;

        if (!CanSync)
        {
            IsSyncEnabled = false;
            IsTimelinePlaybackActive = false;
            return;
        }

        if (IsSyncEnabled)
        {
            SeekToCursorIfPresent();
        }
    }

    public void SetVideoSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return;
        }

        VideoWidth = width;
        VideoHeight = height;
    }

    partial void OnIsTimelinePlaybackActiveChanged(bool value)
    {
        // Plots suppress pointer-driven cursor updates while the flag is set.
        timeline.SetPlaybackActive(value);
    }

    partial void OnIsSyncEnabledChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        if (!CanSync)
        {
            IsSyncEnabled = false;
            return;
        }

        PauseBackend();
        SeekToCursorIfPresent();
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        if (backend is null)
        {
            return;
        }

        if (backend.IsPlaying)
        {
            backend.Pause();
            RefreshPlaybackState();

            if (IsSyncEnabled && !IsTimelinePlaybackActive)
            {
                SeekToCursorIfPresent();
            }
        }
        else
        {
            backend.Play();
            RefreshPlaybackState();
        }
    }

    private bool CanPlayPause() => backend is not null;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private void ToggleSync()
    {
        IsSyncEnabled = !IsSyncEnabled;
    }

    private void OnBackendPlaybackStateChanged(object? sender, EventArgs e)
    {
        RefreshPlaybackState();

        if (!IsPlaying)
        {
            IsTimelinePlaybackActive = false;
        }
    }

    private void OnBackendPositionChanged(object? sender, VideoPositionChangedEventArgs e)
    {
        if (!IsTimelinePlaybackActive ||
            playbackContext?.TelemetryDurationSeconds is not { } telemetryDuration)
        {
            return;
        }

        var telemetrySeconds = e.Position.TotalSeconds
                               - playbackContext.SessionOffsetSeconds
                               - playbackContext.AlignmentOffsetSeconds;
        timeline.SetCursorPosition(Math.Clamp(telemetrySeconds / telemetryDuration, 0, 1));
    }

    private void OnTimelinePlaybackToggleRequested(object? sender, EventArgs e)
    {
        if (IsTimelinePlaybackActive)
        {
            StopTimelinePlayback();
            return;
        }

        if (backend is null || playbackContext?.CanSync != true)
        {
            return;
        }

        IsTimelinePlaybackActive = true;
        backend.Play();
        SeekToCursorIfPresent();
        RefreshPlaybackState();
    }

    private void OnTimelinePlaybackStopRequested(object? sender, EventArgs e)
    {
        if (IsTimelinePlaybackActive)
        {
            StopTimelinePlayback();
        }
    }

    private void StopTimelinePlayback()
    {
        IsTimelinePlaybackActive = false;
        PauseBackend();
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IRecordedSessionTimeline.NormalizedCursorPosition) &&
            IsSyncEnabled &&
            !IsTimelinePlaybackActive &&
            !IsPlaying)
        {
            SeekToCursorIfPresent();
        }
    }

    private void PauseBackend()
    {
        if (backend is null)
        {
            return;
        }

        if (backend.IsPlaying)
        {
            backend.Pause();
        }

        RefreshPlaybackState();
    }

    private void SeekToCursorIfPresent()
    {
        if (backend is null ||
            timeline.NormalizedCursorPosition is not { } cursor ||
            playbackContext?.TelemetryDurationSeconds is not { } telemetryDuration)
        {
            return;
        }

        var telemetrySeconds = cursor * telemetryDuration;
        var videoSeconds = playbackContext.SessionOffsetSeconds + playbackContext.AlignmentOffsetSeconds + telemetrySeconds;
        var clampedSeconds = Math.Clamp(videoSeconds, 0, playbackContext.VideoDurationSeconds);
        backend.Seek(TimeSpan.FromSeconds(clampedSeconds));
    }

    private void RefreshPlaybackState()
    {
        IsPlaying = backend?.IsPlaying == true;
    }

    public void Dispose()
    {
        SetBackend(null);
        timeline.PropertyChanged -= OnTimelinePropertyChanged;
        timeline.PlaybackToggleRequested -= OnTimelinePlaybackToggleRequested;
        timeline.PlaybackStopRequested -= OnTimelinePlaybackStopRequested;
    }
}
