namespace Sufni.App.Video.Models;

/// <summary>
/// Time mapping of the active clip: video seconds are the session offset
/// plus the alignment offset plus the telemetry seconds.
/// </summary>
public sealed record VideoPlaybackContext(
    double SessionOffsetSeconds,
    double VideoDurationSeconds,
    double? TelemetryDurationSeconds,
    double AlignmentOffsetSeconds = 0)
{
    public bool CanSync => TelemetryDurationSeconds is > 0;
}
