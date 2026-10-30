using Sufni.Telemetry;
using System;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Store;

/// <summary>
/// Immutable view of a session as currently known to the store.
/// Telemetry data (the psst blob) and full track points are not part
/// of the snapshot — they are loaded on demand by the editor via
/// <see cref="Services.ISessionRepository"/>. The
/// <see cref="HasProcessedData"/> flag is the discriminator the
/// editor uses to detect "telemetry just became available, reload it"
/// transitions on a <c>Watch</c> emission.
/// </summary>
public sealed record SessionSnapshot(
    Guid Id,
    string Name,
    string Description,
    Guid? SetupId,
    long? Timestamp,
    Guid? FullTrackId,
    bool HasProcessedData,
    string? ProcessingFingerprintJson,
    string? FrontSpringRate,
    uint? FrontHighSpeedCompression,
    uint? FrontLowSpeedCompression,
    uint? FrontLowSpeedRebound,
    uint? FrontHighSpeedRebound,
    string? RearSpringRate,
    uint? RearHighSpeedCompression,
    uint? RearLowSpeedCompression,
    uint? RearLowSpeedRebound,
    uint? RearHighSpeedRebound,
    long Updated,
    double? DurationSeconds = null,
    double? DistanceMeters = null,
    double? AscentMeters = null,
    double? DescentMeters = null,
    double GpsOffsetSeconds = 0,
    long ProcessedTelemetryRevision = 0,
    long TrackProjectionRevision = 0,
    Guid? SourceSessionId = null,
    double WindowStartSeconds = 0,
    double? WindowEndSeconds = null,
    double? PreviousWindowStartSeconds = null,
    double? PreviousWindowEndSeconds = null,
    int? VelocityFilterWindowMilliseconds = null)
{
    public RecordedSessionDerivationWindow? DerivationWindow => SourceSessionId is { } sourceSessionId
        ? new RecordedSessionDerivationWindow(sourceSessionId, WindowStartSeconds, WindowEndSeconds)
        : null;

    public Guid RecordingSourceSessionId => SourceSessionId ?? Id;

    public TelemetryProcessingOptions ProcessingOptions => new(
        VelocityFilterWindowMilliseconds ?? TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds);

    public bool CanUndoTrim => PreviousWindowStartSeconds is not null;

    public static SessionSnapshot From(Session session) => new(
        session.Id,
        session.Name,
        session.Description,
        session.Setup,
        session.Timestamp,
        session.FullTrack,
        session.HasProcessedData,
        session.ProcessingFingerprintJson,
        session.FrontSpringRate,
        session.FrontHighSpeedCompression,
        session.FrontLowSpeedCompression,
        session.FrontLowSpeedRebound,
        session.FrontHighSpeedRebound,
        session.RearSpringRate,
        session.RearHighSpeedCompression,
        session.RearLowSpeedCompression,
        session.RearLowSpeedRebound,
        session.RearHighSpeedRebound,
        session.Updated,
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.GpsOffsetSeconds,
        session.ProcessedTelemetryRevision,
        session.TrackProjectionRevision,
        session.SourceSessionId,
        session.WindowStartSeconds,
        session.WindowEndSeconds,
        session.PreviousWindowStartSeconds,
        session.PreviousWindowEndSeconds,
        session.VelocityFilterWindowMilliseconds);
}

public static class SessionSnapshotExtensions
{
    public static Session ToMetadataEntity(this SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Session(
            snapshot.Id,
            snapshot.Name,
            snapshot.Description,
            snapshot.SetupId,
            snapshot.Timestamp)
        {
            VelocityFilterWindowMilliseconds = snapshot.VelocityFilterWindowMilliseconds,
            DurationSeconds = snapshot.DurationSeconds,
            DistanceMeters = snapshot.DistanceMeters,
            AscentMeters = snapshot.AscentMeters,
            DescentMeters = snapshot.DescentMeters,
            FullTrack = snapshot.FullTrackId,
            HasProcessedData = snapshot.HasProcessedData,
            ProcessingFingerprintJson = snapshot.ProcessingFingerprintJson,
            GpsOffsetSeconds = snapshot.GpsOffsetSeconds,
            ProcessedTelemetryRevision = snapshot.ProcessedTelemetryRevision,
            TrackProjectionRevision = snapshot.TrackProjectionRevision,
            SourceSessionId = snapshot.SourceSessionId,
            WindowStartSeconds = snapshot.WindowStartSeconds,
            WindowEndSeconds = snapshot.WindowEndSeconds,
            PreviousWindowStartSeconds = snapshot.PreviousWindowStartSeconds,
            PreviousWindowEndSeconds = snapshot.PreviousWindowEndSeconds,
            FrontSpringRate = snapshot.FrontSpringRate,
            FrontHighSpeedCompression = snapshot.FrontHighSpeedCompression,
            FrontLowSpeedCompression = snapshot.FrontLowSpeedCompression,
            FrontLowSpeedRebound = snapshot.FrontLowSpeedRebound,
            FrontHighSpeedRebound = snapshot.FrontHighSpeedRebound,
            RearSpringRate = snapshot.RearSpringRate,
            RearHighSpeedCompression = snapshot.RearHighSpeedCompression,
            RearLowSpeedCompression = snapshot.RearLowSpeedCompression,
            RearLowSpeedRebound = snapshot.RearLowSpeedRebound,
            RearHighSpeedRebound = snapshot.RearHighSpeedRebound,
            Updated = snapshot.Updated,
        };
    }
}
