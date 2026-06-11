using System.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.TestSupport;

public sealed class TestRecordedSessionTimeline : IRecordedSessionTimeline
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? VisibleRangeChanged;
    public event EventHandler? PlaybackToggleRequested;
    public event EventHandler? PlaybackStopRequested;

    public double? NormalizedCursorPosition { get; private set; }
    public bool IsPlaybackActive { get; private set; }
    public double VisibleRangeStart { get; private set; }
    public double VisibleRangeEnd { get; private set; } = 1;
    public object? VisibleRangeChangeSource { get; private set; }

    public void SetCursorPosition(double? normalizedPosition)
    {
        NormalizedCursorPosition = normalizedPosition;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NormalizedCursorPosition)));
    }

    public void SetPlaybackActive(bool active)
    {
        IsPlaybackActive = active;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaybackActive)));
    }

    public void SetVisibleRange(double start, double end, object source)
    {
        VisibleRangeStart = start;
        VisibleRangeEnd = end;
        VisibleRangeChangeSource = source;
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
        SetVisibleRange(0, 1, this);
        SetCursorPosition(null);
    }
}
