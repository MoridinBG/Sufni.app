using System;
using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>A pure processed result, ready for its workflow owner to associate and persist.</summary>
public sealed record ProcessedGeneration(
    ProcessedTelemetryPayload ProcessedTelemetry,
    IReadOnlyList<TrackPoint>? TrackPoints)
{
    public static ProcessedGeneration Create(ProcessedTelemetryPayload telemetry, IReadOnlyList<TrackPoint>? points) =>
        new(telemetry, points is { Count: > 0 } ? points : null);

    public Session CreateSession(SessionSnapshot metadata)
    {
        var session = metadata.ToMetadataEntity();
        session.ProcessedData = ProcessedTelemetry.Data;
        session.ProcessingFingerprintJson = ProcessedTelemetry.FingerprintJson;
        session.DurationSeconds = SessionDuration.Normalize(ProcessedTelemetry.TelemetryData.Metadata.Duration);
        return session;
    }
}
