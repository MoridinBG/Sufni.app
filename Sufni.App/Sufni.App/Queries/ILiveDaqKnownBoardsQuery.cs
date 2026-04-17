using System;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.Queries;

public sealed record LiveDaqTravelChannelCalibration(
    double MaxTravel,
    Func<ushort, double> MeasurementToTravel);

public sealed record LiveDaqTravelCalibration(
    LiveDaqTravelChannelCalibration? Front,
    LiveDaqTravelChannelCalibration? Rear);

public sealed record KnownLiveDaqRecord(
    string IdentityKey,
    string DisplayName,
    string BoardId,
    Guid? SetupId,
    string? SetupName,
    Guid? BikeId,
    string? BikeName);

public sealed record LiveDaqSessionContext(
    string IdentityKey,
    Guid BoardId,
    string DisplayName,
    Guid SetupId,
    string SetupName,
    Guid BikeId,
    string BikeName,
    BikeData BikeData,
    LiveDaqTravelCalibration TravelCalibration);

// Read-only query that merges persisted boards with their current setup and bike
// context for the live DAQ list.
public interface ILiveDaqKnownBoardsQuery
{
    // Replays the latest projection on subscribe and pushes updates when the
    // underlying board, setup, or bike data changes.
    IObservable<IReadOnlyList<KnownLiveDaqRecord>> Changes { get; }

    // Returns the latest enriched record for one identity key.
    KnownLiveDaqRecord? Get(string identityKey);

    // Returns the current per-side travel calibration for one identity key.
    LiveDaqTravelCalibration? GetTravelCalibration(string identityKey);

    // Returns the session-ready calibration and bike context for one identity key.
    LiveDaqSessionContext? GetSessionContext(string identityKey);
}