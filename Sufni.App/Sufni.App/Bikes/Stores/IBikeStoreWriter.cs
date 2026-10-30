using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Bikes.Stores;

/// Write surface for the bike store.
public interface IBikeStoreWriter : IBikeStore
{
    /// Replace the published contents with prepared snapshots.
    Task ReplaceAllAsync(IReadOnlyList<BikeSnapshot> snapshots);

    Task<StoreMutationResult<BikeSnapshot>> CommitBikeAsync(
        Bike bike,
        long? baselineUpdated = null);

    Task<StoreDeleteResult<BikeSnapshot>> CommitBikeDeleteAsync(
        Guid bikeId);

    Task PublishBikesChangedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default);

    Task PublishBikesRemovedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default);
}
