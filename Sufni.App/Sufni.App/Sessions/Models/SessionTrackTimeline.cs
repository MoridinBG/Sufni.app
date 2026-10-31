using System;

namespace Sufni.App.Sessions.Models;

/// <summary>Maps session-relative telemetry seconds to the aligned GPS clock.</summary>
public readonly record struct SessionTrackTimeline(double TelemetryOriginSeconds, double GpsOffsetSeconds = 0)
{
    public double TrackOriginSeconds => TelemetryOriginSeconds + NormalizeGpsOffsetSeconds(GpsOffsetSeconds);

    public double ToTrackSeconds(double telemetrySeconds) => TrackOriginSeconds + telemetrySeconds;

    public double ToTelemetrySeconds(double trackSeconds) => trackSeconds - TrackOriginSeconds;

    public static double NormalizeGpsOffsetSeconds(double offset) => double.IsFinite(offset) ? offset : 0;
}
