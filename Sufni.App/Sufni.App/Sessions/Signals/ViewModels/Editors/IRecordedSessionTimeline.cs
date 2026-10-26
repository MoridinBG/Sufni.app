using System;
using System.ComponentModel;

namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

public interface IRecordedSessionTimeline : INotifyPropertyChanged
{
    event EventHandler? VisibleRangeChanged;
    event EventHandler? PlaybackToggleRequested;
    event EventHandler? PlaybackStopRequested;

    double? NormalizedCursorPosition { get; }
    bool IsPlaybackActive { get; }
    double VisibleRangeStart { get; }
    double VisibleRangeEnd { get; }
    object? VisibleRangeChangeSource { get; }

    void SetCursorPosition(double? normalizedPosition);
    void SetPlaybackActive(bool active);
    void SetVisibleRange(double start, double end, object source);
    void RequestPlaybackToggle();
    void RequestPlaybackStop();
    void Reset();
}
