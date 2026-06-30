using System;
using System.Linq;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Base;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Bikes.Stores;

/// <summary>
/// Single source of truth for "what bikes exist". Loaded once at startup
/// and updated by coordinators via <see cref="IBikeStoreWriter"/>.
/// Registered as a singleton behind both <see cref="IBikeStore"/> and
/// <see cref="IBikeStoreWriter"/>.
/// </summary>
internal sealed class BikeStore(ISynchronizableRepository<Bike> bikeRepository)
    : SourceCacheStoreBase<BikeSnapshot, Guid>(b => b.Id), IBikeStoreWriter
{
    public async Task RefreshAsync()
    {
        var bikes = await bikeRepository.GetAllAsync();
        ReplaceWith(bikes.Select(BikeSnapshot.From));
    }
}
