using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.Sessions.Models;

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

        return IsAlignedTo(trackPoints, GetSessionStartSeconds(telemetryData, gpsOffsetSeconds));
    }

    // Alignment check driven by the session row's own timestamp, for callers that
    // would otherwise deserialize the whole processed-telemetry blob just to read
    // its metadata. The cached session-window track is generated from
    // session.Timestamp (see SessionTelemetryWriter), so that is the value it must
    // line up with.
    public static bool IsSessionTrackAligned(
        IReadOnlyList<TrackPoint>? trackPoints,
        long? timestamp,
        double gpsOffsetSeconds)
    {
        if (trackPoints is null || trackPoints.Count == 0 || timestamp is not { } value)
        {
            return false;
        }

        return IsAlignedTo(trackPoints, value + NormalizeGpsOffsetSeconds(gpsOffsetSeconds));
    }

    private static bool IsAlignedTo(IReadOnlyList<TrackPoint> trackPoints, double expectedStartSeconds) =>
        Math.Abs(trackPoints[0].Time - expectedStartSeconds) <= AlignmentToleranceSeconds;

    public static double NormalizeGpsOffsetSeconds(double gpsOffsetSeconds) =>
        double.IsFinite(gpsOffsetSeconds) ? gpsOffsetSeconds : 0;

    private static double GetSessionStartSeconds(
        TelemetryData telemetryData,
        double gpsOffsetSeconds) =>
        telemetryData.Metadata.Timestamp + NormalizeGpsOffsetSeconds(gpsOffsetSeconds);
}
