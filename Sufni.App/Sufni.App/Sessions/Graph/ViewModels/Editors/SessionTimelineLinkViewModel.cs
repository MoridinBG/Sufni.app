using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.Sessions.Graph.ViewModels.Editors;

public sealed partial class SessionTimelineLinkViewModel : ObservableObject, IRecordedSessionTimeline
{
    private const double Epsilon = 0.000001;

    [ObservableProperty] public partial double? NormalizedCursorPosition { get; set; }
    [ObservableProperty] public partial bool IsPlaybackActive { get; set; }
    [ObservableProperty] public partial double VisibleRangeStart { get; set; }
    [ObservableProperty] public partial double VisibleRangeEnd { get; set; } = 1;

    public event EventHandler? VisibleRangeChanged;
    public event EventHandler? PlaybackToggleRequested;
    public event EventHandler? PlaybackStopRequested;

    public object? VisibleRangeChangeSource { get; private set; }

    public void SetCursorPosition(double? normalizedPosition)
    {
        double? clamped = normalizedPosition is null
            ? null
            : Math.Clamp(normalizedPosition.Value, 0, 1);

        if ((clamped, NormalizedCursorPosition) is (null, null))
        {
            return;
        }

        if (clamped is not null &&
            NormalizedCursorPosition is not null &&
            AreClose(clamped.Value, NormalizedCursorPosition.Value))
        {
            return;
        }

        NormalizedCursorPosition = clamped;

        if (IsPlaybackActive && clamped is { } cursor)
        {
            KeepPlaybackCursorVisible(cursor);
        }
    }

    private void KeepPlaybackCursorVisible(double cursor)
    {
        var span = VisibleRangeEnd - VisibleRangeStart;
        if (span <= 0 || (cursor >= VisibleRangeStart && cursor <= VisibleRangeEnd))
        {
            return;
        }

        // Pan only — the span (zoom) is kept and the cursor re-enters at the
        // window edge, clamped so the window never runs past the timeline.
        var start = Math.Min(cursor, 1 - span);
        SetVisibleRange(start, start + span, this);
    }

    public void ClearCursorPosition()
    {
        SetCursorPosition(null);
    }

    public void SetPlaybackActive(bool active)
    {
        IsPlaybackActive = active;
    }

    public void SetVisibleRange(double start, double end, object? source = null)
    {
        if (double.IsNaN(start) || double.IsNaN(end) || double.IsInfinity(start) || double.IsInfinity(end))
        {
            return;
        }

        start = Math.Clamp(start, 0, 1);
        end = Math.Clamp(end, 0, 1);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (AreClose(start, VisibleRangeStart) && AreClose(end, VisibleRangeEnd))
        {
            return;
        }

        VisibleRangeChangeSource = source;
        VisibleRangeStart = start;
        VisibleRangeEnd = end;
        VisibleRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPlaybackToggle()
    {
        PlaybackToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPlaybackStop()
    {
        PlaybackStopRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        ClearCursorPosition();
        SetPlaybackActive(false);
        SetVisibleRange(0, 1);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= Epsilon;
    }
}
