using System;
using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;

namespace Sufni.App.MapsAndTracks.Queries;

public static class SessionGpsMetricsQuery
{
    public static (GpsMetricsKey Key, SessionTrackKey? Track) Evaluate(long? timestamp, double? durationSeconds,
        double gpsOffsetSeconds, double windowStartSeconds, IReadOnlyList<TrackSnapshot> tracks)
    {
        var duration = SessionDuration.Normalize(durationSeconds);
        var offset = SessionTrackTimeline.NormalizeGpsOffsetSeconds(gpsOffsetSeconds);
        var selected = SessionTrackSelection.Select(tracks, timestamp, duration, offset, windowStartSeconds);
        var key = new GpsMetricsKey(selected?.Id, timestamp, duration, offset,
            SessionTrackTimeline.FractionalOrigin(windowStartSeconds), GpsTrackPointProjection.ProjectionVersion,
            GpsMetricsCalculator.AlgorithmVersion);
        SessionTrackKey? track = selected is null ? null : new SessionTrackKey(
            selected.Id, selected.PointsRevision, timestamp!.Value, duration!.Value, offset, WindowOriginFraction: key.WindowOriginFraction);
        return (key, track);
    }
}
