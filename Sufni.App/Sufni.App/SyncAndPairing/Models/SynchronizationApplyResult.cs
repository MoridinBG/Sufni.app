using System;
using System.Collections.Generic;
using Sufni.App.Sessions.Services;

namespace Sufni.App.SyncAndPairing.Models;

/// <summary>Reconciled row identities and pending generation transfers from one merge transaction.</summary>
public sealed record SynchronizationApplyResult(
    IReadOnlyList<Guid> BikeIds,
    IReadOnlyList<Guid> SetupIds,
    IReadOnlyList<Guid> BoardIds,
    IReadOnlyList<Guid> TrackIds,
    IReadOnlyList<Guid> SessionIds,
    IReadOnlyList<Guid> SegmentIds,
    IReadOnlyList<Guid> CustomLayerIds,
    IReadOnlyList<SessionBlobSwap> Swaps);
