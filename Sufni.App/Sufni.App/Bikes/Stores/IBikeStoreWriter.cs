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
    /// Load all bikes from the database and replace the current contents.
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<StoreMutationResult<BikeSnapshot>> CommitBikeAsync(
        Bike bike,
        long? baselineUpdated = null,
        CancellationToken cancellationToken = default);

    Task<StoreDeleteResult<BikeSnapshot>> CommitBikeDeleteAsync(
        Guid bikeId,
        CancellationToken cancellationToken = default);

    Task PublishBikesChangedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default);

    Task PublishBikesRemovedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default);
}
