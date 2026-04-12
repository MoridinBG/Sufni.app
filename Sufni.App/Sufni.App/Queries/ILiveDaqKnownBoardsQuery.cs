using System;
using System.Collections.Generic;

namespace Sufni.App.Queries;

public sealed record KnownLiveDaqRecord(
    string IdentityKey,
    string DisplayName,
    string BoardId,
    Guid? SetupId,
    string? SetupName,
    Guid? BikeId,
    string? BikeName);

// Read-only query that merges persisted boards with their current setup and bike
// context for the live DAQ list.
public interface ILiveDaqKnownBoardsQuery
{
    // Replays the latest projection on subscribe and pushes updates when the
    // underlying board, setup, or bike data changes.
    IObservable<IReadOnlyList<KnownLiveDaqRecord>> Changes { get; }
}