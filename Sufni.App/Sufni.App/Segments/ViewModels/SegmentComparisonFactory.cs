using System;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Segments.Coordinators;

namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// Creates the segment comparison of a session editor.
/// </summary>
public interface ISegmentComparisonFactory
{
    /// <summary>
    /// <paramref name="setTimelineVisibleRange"/> receives the normalized start and end.
    /// </summary>
    SegmentComparisonViewModel Create(
        Guid sessionId,
        IObservable<SegmentComparisonSessionState> sessionState,
        Action<double, double> setTimelineVisibleRange,
        Action<string> reportError,
        Action<string> reportNotification);
}

internal sealed class SegmentComparisonFactory(
    ISegmentCoordinator segmentCoordinator,
    ITrackCoordinator trackCoordinator) : ISegmentComparisonFactory
{
    public SegmentComparisonViewModel Create(
        Guid sessionId,
        IObservable<SegmentComparisonSessionState> sessionState,
        Action<double, double> setTimelineVisibleRange,
        Action<string> reportError,
        Action<string> reportNotification) =>
        new(sessionId, sessionState, segmentCoordinator, trackCoordinator, setTimelineVisibleRange, reportError, reportNotification);
}
