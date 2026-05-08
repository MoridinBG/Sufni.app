using System;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

internal static class RecordedTimeSeriesMarkerHitTester
{
    public static bool TryGetHitMarkerSeconds(
        TelemetryData? telemetry,
        double pointerSeconds,
        double thresholdSeconds,
        out double markerSeconds)
    {
        markerSeconds = default;
        if (telemetry is null || telemetry.Markers.Length == 0)
        {
            return false;
        }

        foreach (var marker in telemetry.Markers)
        {
            if (double.IsNaN(marker.TimestampOffset) || double.IsInfinity(marker.TimestampOffset))
            {
                continue;
            }

            var markerSecondsCandidate = telemetry.Metadata.Duration > 0
                ? Math.Clamp(marker.TimestampOffset, 0, telemetry.Metadata.Duration)
                : marker.TimestampOffset;
            if (Math.Abs(markerSecondsCandidate - pointerSeconds) <= thresholdSeconds)
            {
                markerSeconds = markerSecondsCandidate;
                return true;
            }
        }

        return false;
    }
}
