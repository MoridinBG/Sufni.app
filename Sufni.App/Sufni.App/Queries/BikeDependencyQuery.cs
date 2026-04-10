using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
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
        // Project the cache's change stream into a void signal that
        // delete-aware view models subscribe to. Each subscriber gets
        // an initial emit (the cache's initial change set) plus one
        // emit per subsequent setup add/remove/update.
        Changes = setupCache.Connect().Select(_ => Unit.Default);
    }

    public async Task<bool> IsBikeInUseAsync(Guid bikeId)
    {
        var setups = await databaseService.GetAllAsync<Setup>();
        return setups.Any(s => s?.BikeId == bikeId);
    }

    public bool IsBikeInUse(Guid bikeId) =>
        setupCache.Items.Any(s => s.BikeId == bikeId);

    public IObservable<Unit> Changes { get; }

    public void Dispose() => setupCache.Dispose();
}
