using System;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Queries;

public sealed class BikeDependencyQuery : IBikeDependencyQuery, IDisposable
{
    private readonly IDatabaseService databaseService;
    private readonly IObservableCache<SetupSnapshot, Guid> setupCache;

    public BikeDependencyQuery(IDatabaseService databaseService, ISetupStore setupStore)
    {
        this.databaseService = databaseService;
        // Materialize the setup store into a sync cache so the
        // IsBikeInUse fast path can answer immediately. Disposed when
        // the singleton is disposed at app shutdown.
        setupCache = setupStore.Connect().AsObservableCache();
    }

    public async Task<bool> IsBikeInUseAsync(Guid bikeId)
    {
        var setups = await databaseService.GetAllAsync<Setup>();
        return setups.Any(s => s?.BikeId == bikeId);
    }

    public bool IsBikeInUse(Guid bikeId) =>
        setupCache.Items.Any(s => s.BikeId == bikeId);

    public void Dispose() => setupCache.Dispose();
}
