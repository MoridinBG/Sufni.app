using System;
using System.ComponentModel;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionTimeline : INotifyPropertyChanged
{
    event EventHandler? VisibleRangeChanged;

    double? NormalizedCursorPosition { get; }
    double VisibleRangeStart { get; }
    double VisibleRangeEnd { get; }
    object? VisibleRangeChangeSource { get; }

    void SetCursorPosition(double? normalizedPosition);
    void SetVisibleRange(double start, double end, object source);
    void Reset();
}
