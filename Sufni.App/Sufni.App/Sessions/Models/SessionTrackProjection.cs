using System;
using System.Collections.Generic;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.Sessions.Models;

internal static class SessionTrackProjection
{
    public static List<TrackPoint> GenerateSessionTrack(
        Track fullTrack,
        TelemetryData telemetryData,
        double gpsOffsetSeconds)
    {
        var timeline = new SessionTrackTimeline(telemetryData.Metadata.Timestamp, gpsOffsetSeconds);
        var start = timeline.ToTrackSeconds(0);
        var end = timeline.ToTrackSeconds(Math.Ceiling(telemetryData.Metadata.Duration));
        return fullTrack.GenerateSessionTrack(start, end);
    }

    public static List<TrackPoint>? GenerateSessionTrack(
        Track fullTrack,
        long? timestamp,
        double? durationSeconds,
        double gpsOffsetSeconds = 0)
    {
        if (!timestamp.HasValue ||
            durationSeconds is not { } duration ||
            !double.IsFinite(duration) ||
            duration <= 0 ||
            !fullTrack.HasPoints)
        {
            return null;
        }

        var timeline = new SessionTrackTimeline(timestamp.Value, gpsOffsetSeconds);
        var start = timeline.ToTrackSeconds(0);
        var end = timeline.ToTrackSeconds((int)Math.Ceiling(duration));
        var points = fullTrack.GenerateSessionTrack(start, end);
        return points.Count == 0 ? null : points;
    }

}
