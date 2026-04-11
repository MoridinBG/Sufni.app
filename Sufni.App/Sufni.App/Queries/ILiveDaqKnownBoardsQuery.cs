using System;
using System.Collections.Generic;
using System.Reactive;

namespace Sufni.App.Queries;

public sealed record KnownLiveDaqRecord(
    string IdentityKey,
    string DisplayName,
    string BoardId,
    Guid? SetupId,
    string? SetupName,
    Guid? BikeId,
    string? BikeName);

public interface ILiveDaqKnownBoardsQuery
{
    IReadOnlyList<KnownLiveDaqRecord> GetCurrent();

    IObservable<Unit> Changes { get; }
}