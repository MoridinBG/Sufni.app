using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Bikes.Stores;

/// Write surface for the bike store.
public interface IBikeStoreWriter : IBikeStore
{
    /// Replace the published contents with prepared snapshots.
    Task ReplaceAllAsync(IReadOnlyList<BikeSnapshot> snapshots);

    Task PublishBikesChangedAsync(
        IReadOnlyCollection<BikeSnapshot> snapshots,
        CancellationToken cancellationToken = default);

    Task PublishBikesRemovedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default);
}
