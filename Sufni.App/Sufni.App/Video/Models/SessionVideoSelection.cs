using Sufni.App.Sessions.Models;
using System;
using System.Collections.Generic;
using System.Linq;

using Sufni.App.Video.Stores;
namespace Sufni.App.Video.Models;

/// <summary>
/// Picks the clip to show at a telemetry time. Clips whose file is present
/// and that contain the time win, the latest added first; otherwise the
/// nearest clip wins.
/// </summary>
public static class SessionVideoSelection
{
    public static IEnumerable<SessionVideoSnapshot> ForWindow(IEnumerable<SessionVideoSnapshot> clips,
        string? ownerKey, SessionTrackTimeline timeline, double? durationSeconds) =>
        ownerKey is not null && durationSeconds is > 0
            ? clips.Where(clip => clip.OwnerKey == ownerKey && timeline.OverlapsOwnerRange(
                -clip.AlignmentOffsetSeconds, clip.DurationSeconds - clip.AlignmentOffsetSeconds, durationSeconds.Value))
            : [];

    public static SessionVideoSnapshot? Select(
        IEnumerable<SessionVideoSnapshot> clips,
        SessionTrackTimeline? timeline,
        double telemetrySeconds)
    {
        return clips
            .Where(clip => clip.FileExists)
            .Select(clip => (Clip: clip, Distance: DistanceSeconds(clip, timeline, telemetrySeconds)))
            .OrderBy(candidate => candidate.Distance)
            .ThenByDescending(candidate => candidate.Clip.Added)
            .ThenByDescending(candidate => candidate.Clip.Id)
            .Select(candidate => candidate.Clip)
            .FirstOrDefault();
    }

    private static double DistanceSeconds(
        SessionVideoSnapshot clip,
        SessionTrackTimeline? timeline,
        double telemetrySeconds)
    {
        var videoSeconds = VideoPlaybackContext.FromTimeline(clip, timeline, telemetryDurationSeconds: null).ToVideoSeconds(telemetrySeconds);
        if (videoSeconds < 0)
        {
            return -videoSeconds;
        }

        return Math.Max(0, videoSeconds - clip.DurationSeconds);
    }
}
