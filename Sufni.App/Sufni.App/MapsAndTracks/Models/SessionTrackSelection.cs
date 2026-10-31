using System;
using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;

namespace Sufni.App.MapsAndTracks.Models;

/// <summary>Selects the greatest overlap with the aligned session window; ties use the lowest ID.</summary>
public static class SessionTrackSelection
{
    public static TrackSnapshot? Select(IEnumerable<TrackSnapshot> tracks, long? timestamp,
        double? durationSeconds, double gpsOffsetSeconds, double windowStartSeconds = 0)
    {
        if (timestamp is null || durationSeconds is not { } duration || !double.IsFinite(duration) || duration <= 0)
            return null;

        var timeline = SessionTrackTimeline.FromSessionWindow(timestamp.Value, gpsOffsetSeconds, windowStartSeconds);
        var start = timeline.ToTrackSeconds(0);
        var end = timeline.ToTrackSeconds(duration);
        TrackSnapshot? selected = null;
        var greatestOverlap = 0.0;
        foreach (var track in tracks)
        {
            var overlap = Math.Min(end, track.EndTime) - Math.Max(start, track.StartTime);
            if (overlap > greatestOverlap || overlap > 0 && overlap == greatestOverlap &&
                selected is not null && track.Id.CompareTo(selected.Id) < 0)
            {
                selected = track;
                greatestOverlap = overlap;
            }
        }
        return selected;
    }
}
