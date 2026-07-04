using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Bikes.Stores;

/// <summary>
/// Single source of truth for "what bikes exist". Loaded once at startup
/// and updated by coordinators via <see cref="IBikeStoreWriter"/>.
/// Registered as a singleton behind both <see cref="IBikeStore"/> and
/// <see cref="IBikeStoreWriter"/>.
/// </summary>
internal sealed class BikeStore(
    ISynchronizableRepository<Bike> bikeRepository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<BikeSnapshot, Guid>(b => b.Id, uiThreadDispatcher), IBikeStoreWriter
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bikes = await bikeRepository.GetAllAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(bikes.Select(BikeSnapshot.From));
    }

    public async Task<StoreMutationResult<BikeSnapshot>> CommitBikeAsync(
        Bike bike,
        long? baselineUpdated = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (baselineUpdated.HasValue)
        {
            var current = Get(bike.Id);
            if (current is not null && current.Updated > baselineUpdated.Value)
            {
                return new StoreMutationResult<BikeSnapshot>.Conflict(current);
            }
        }

        try
        {
            await bikeRepository.PutAsync(bike);
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = BikeSnapshot.From(bike);
            await PublishSnapshotAsync(snapshot);
            return new StoreMutationResult<BikeSnapshot>.Saved(snapshot);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<BikeSnapshot>.Failed(e.Message);
        }
    }

    public async Task<StoreDeleteResult<BikeSnapshot>> CommitBikeDeleteAsync(
        Guid bikeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = Get(bikeId);

        try
        {
            await bikeRepository.DeleteAsync(bikeId);
            cancellationToken.ThrowIfCancellationRequested();
            await PublishRemoveAsync(bikeId);
            return new StoreDeleteResult<BikeSnapshot>.Deleted(previous);
        }
        catch (Exception e)
        {
            return new StoreDeleteResult<BikeSnapshot>.Failed(e.Message);
        }
    }

    public async Task PublishBikesChangedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<BikeSnapshot>();
        var removedIds = new List<Guid>();

        foreach (var bikeId in bikeIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bike = await bikeRepository.GetAsync(bikeId);
            if (bike is null)
            {
                removedIds.Add(bikeId);
            }
            else
            {
                snapshots.Add(BikeSnapshot.From(bike));
            }
        }

        if (snapshots.Count > 0)
        {
            await PublishSnapshotsAsync(snapshots);
        }

        if (removedIds.Count > 0)
        {
            await PublishRemovalsAsync(removedIds);
        }
    }

    public Task PublishBikesRemovedAsync(
        IReadOnlyCollection<Guid> bikeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(bikeIds.Distinct());
    }
}
