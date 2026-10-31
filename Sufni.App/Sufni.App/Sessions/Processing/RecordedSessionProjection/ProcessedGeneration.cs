using System;
using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>A pure processed result, ready for its workflow owner to associate and persist.</summary>
public sealed record ProcessedGeneration(
    ProcessedTelemetryPayload ProcessedTelemetry,
    SessionSummaryMetrics Metrics,
    IReadOnlyList<TrackPoint>? TrackPoints)
{
    public static ProcessedGeneration Create(ProcessedTelemetryPayload telemetry, IReadOnlyList<TrackPoint>? points) =>
        new(telemetry, SessionSummaryMetricsCalculator.Calculate(telemetry.TelemetryData.Metadata.Duration, points),
            points is { Count: > 0 } ? points : null);

    public ProcessedGeneration WithTrackWindow(Track track, SessionSnapshot session) =>
        Create(ProcessedTelemetry, SessionTrackProjection.GenerateSessionTrack(
            track, session.Timestamp, Metrics.DurationSeconds, session.GpsOffsetSeconds));

    public Session CreateSession(SessionSnapshot metadata, Guid? fullTrackId)
    {
        var session = metadata.ToMetadataEntity();
        session.ProcessedData = ProcessedTelemetry.Data;
        session.ProcessingFingerprintJson = ProcessedTelemetry.FingerprintJson;
        session.FullTrack = fullTrackId;
        session.DurationSeconds = Metrics.DurationSeconds;
        session.DistanceMeters = Metrics.DistanceMeters;
        session.AscentMeters = Metrics.AscentMeters;
        session.DescentMeters = Metrics.DescentMeters;
        return session;
    }
}
