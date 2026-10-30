using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.Bikes.Stores;

internal sealed class BikeStore(StorePublication publication)
    : SourceCacheStoreBase<BikeSnapshot, Guid>(bike => bike.Id, publication), IBikeStoreWriter
{
    public Task PublishBikesChangedAsync(IReadOnlyCollection<BikeSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishSnapshotsAsync(snapshots);
    }

    public Task PublishBikesRemovedAsync(IReadOnlyCollection<Guid> bikeIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(bikeIds);
    }
}
