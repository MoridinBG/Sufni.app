using Sufni.App.Sessions.Models;
using Sufni.App.Video.Stores;

namespace Sufni.App.Video.Models;

/// <summary>
/// Time mapping of the active clip: video seconds are the source-relative
/// session origin plus the owner alignment offset plus telemetry seconds.
/// </summary>
public sealed record VideoPlaybackContext(
    double SessionOffsetSeconds,
    double VideoDurationSeconds,
    double? TelemetryDurationSeconds,
    double AlignmentOffsetSeconds = 0)
{
    public bool CanSync => TelemetryDurationSeconds is > 0;

    public static VideoPlaybackContext FromTimeline(SessionVideoSnapshot clip,
        SessionTrackTimeline? timeline, double? telemetryDurationSeconds) => new(
            timeline?.OwnerOriginSeconds ?? 0,
            clip.DurationSeconds, telemetryDurationSeconds, clip.AlignmentOffsetSeconds);

    public double ToVideoSeconds(double telemetrySeconds) =>
        SessionOffsetSeconds + AlignmentOffsetSeconds + telemetrySeconds;

    public double ToTelemetrySeconds(double videoSeconds) =>
        videoSeconds - SessionOffsetSeconds - AlignmentOffsetSeconds;
}
