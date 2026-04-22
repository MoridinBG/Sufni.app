using System;
using Sufni.App.Models;

namespace Sufni.App.Stores;

/// <summary>
/// Immutable view of a session as currently known to the store.
/// Telemetry data (the psst blob) and full track points are not part
/// of the snapshot — they are loaded on demand by the editor via
/// <see cref="Services.IDatabaseService"/>. The
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
    long Updated)
{
    public static SessionSnapshot From(Session session) => new(
        session.Id,
        session.Name,
        session.Description,
        session.Setup,
        session.Timestamp,
        session.FullTrack,
        session.HasProcessedData,
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
        session.Updated);
}
