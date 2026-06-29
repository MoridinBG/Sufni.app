using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

namespace Sufni.App.Models;

internal static class SessionTrackProjection
{
    private const double AlignmentToleranceSeconds = 1e-6;

    public static List<TrackPoint> GenerateSessionTrack(
        Track fullTrack,
        TelemetryData telemetryData,
        double gpsOffsetSeconds)
    {
        var start = GetSessionStartSeconds(telemetryData, gpsOffsetSeconds);
        var end = start + Math.Ceiling(telemetryData.Metadata.Duration);
        return fullTrack.GenerateSessionTrack(start, end);
    }

    public static bool IsSessionTrackAligned(
        IReadOnlyList<TrackPoint>? trackPoints,
        TelemetryData telemetryData,
        double gpsOffsetSeconds)
    {
        if (trackPoints is null || trackPoints.Count == 0)
        {
            return false;
        }

        var expectedStart = GetSessionStartSeconds(telemetryData, gpsOffsetSeconds);
        return Math.Abs(trackPoints[0].Time - expectedStart) <= AlignmentToleranceSeconds;
    }

    public static double NormalizeGpsOffsetSeconds(double gpsOffsetSeconds) =>
        double.IsFinite(gpsOffsetSeconds) ? gpsOffsetSeconds : 0;

    private static double GetSessionStartSeconds(
        TelemetryData telemetryData,
        double gpsOffsetSeconds) =>
        telemetryData.Metadata.Timestamp + NormalizeGpsOffsetSeconds(gpsOffsetSeconds);
}
