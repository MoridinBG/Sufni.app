using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Bikes.Queries;

public sealed class BikeDependencyQuery : IBikeDependencyQuery
{
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly ISetupStore setupStore;

    public BikeDependencyQuery(ISynchronizableRepository<Setup> setupRepository, ISetupStore setupStore)
    {
        this.setupRepository = setupRepository;
        this.setupStore = setupStore;
        Changes = setupStore.Connect().Select(_ => Unit.Default);
    }

    public async Task<bool> IsBikeInUseAsync(Guid bikeId)
    {
        var setups = await setupRepository.GetAllAsync();
        return setups.Any(s => s?.BikeId == bikeId);
    }

    public bool IsBikeInUse(Guid bikeId) =>
        setupStore.IsBikeReferenced(bikeId);

    public IObservable<Unit> Changes { get; }

}
