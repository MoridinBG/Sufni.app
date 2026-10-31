using System;

namespace Sufni.App.Sessions.Models;

/// <summary>Maps telemetry seconds to the aligned GPS clock and recording-source timeline.</summary>
public readonly record struct SessionTrackTimeline(double TelemetryOriginSeconds, double GpsOffsetSeconds = 0, double OwnerOriginSeconds = 0)
{
    public double TrackOriginSeconds => TelemetryOriginSeconds + NormalizeGpsOffsetSeconds(GpsOffsetSeconds);

    public double ToTrackSeconds(double telemetrySeconds) => TrackOriginSeconds + telemetrySeconds;

    public double ToTelemetrySeconds(double trackSeconds) => trackSeconds - TrackOriginSeconds;

    public double TelemetryToOwnerSeconds(double telemetrySeconds) => OwnerOriginSeconds + telemetrySeconds;

    public double OwnerToTelemetrySeconds(double ownerSeconds) => ownerSeconds - OwnerOriginSeconds;

    public bool ContainsOwnerRange(double startSeconds, double endSeconds, double durationSeconds) =>
        double.IsFinite(startSeconds) && double.IsFinite(endSeconds) && startSeconds < endSeconds &&
        startSeconds >= OwnerOriginSeconds && endSeconds <= TelemetryToOwnerSeconds(durationSeconds);

    public static double NormalizeGpsOffsetSeconds(double offset) => double.IsFinite(offset) ? offset : 0;
}
