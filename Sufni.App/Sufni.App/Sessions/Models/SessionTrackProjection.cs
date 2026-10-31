using System;
using System.Collections.Generic;

using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.Sessions.Models;

internal static class SessionTrackProjection
{
    public static List<TrackPoint>? GenerateSessionTrack(
        Track fullTrack,
        long? timestamp,
        double? durationSeconds,
        double gpsOffsetSeconds = 0,
        double windowStartSeconds = 0)
    {
        if (!timestamp.HasValue ||
            durationSeconds is not { } duration ||
            !double.IsFinite(duration) ||
            duration <= 0 ||
            !fullTrack.HasPoints)
        {
            return null;
        }

        var timeline = SessionTrackTimeline.FromSessionWindow(timestamp.Value, gpsOffsetSeconds, windowStartSeconds);
        var start = timeline.ToTrackSeconds(0);
        var end = timeline.ToTrackSeconds((int)Math.Ceiling(duration));
        var points = fullTrack.GenerateSessionTrack(start, end);
        return points.Count == 0 ? null : points;
    }

}
