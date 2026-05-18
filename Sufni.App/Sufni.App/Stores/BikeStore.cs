using System;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

/// <summary>
/// Single source of truth for "what bikes exist". Loaded once at startup
/// and updated by coordinators via <see cref="IBikeStoreWriter"/>.
/// Registered as a singleton behind both <see cref="IBikeStore"/> and
/// <see cref="IBikeStoreWriter"/>.
/// </summary>
internal sealed class BikeStore(IDatabaseService databaseService)
    : SourceCacheStoreBase<BikeSnapshot, Guid>(b => b.Id), IBikeStoreWriter
{
    public async Task RefreshAsync()
    {
        var bikes = await databaseService.GetAllAsync<Bike>();
        ReplaceWith(bikes.Select(BikeSnapshot.From));
    }
}
