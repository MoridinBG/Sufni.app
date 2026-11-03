using System;
using System.Reactive;
using System.Reactive.Linq;

using Sufni.App.Setups.Stores;
namespace Sufni.App.Bikes.Queries;

public sealed class BikeDependencyQuery : IBikeDependencyQuery
{
    private readonly ISetupStore setupStore;

    public BikeDependencyQuery(ISetupStore setupStore)
    {
        this.setupStore = setupStore;
        Changes = setupStore.Connect().Select(_ => Unit.Default);
    }

    public bool IsBikeInUse(Guid bikeId) =>
        setupStore.IsBikeReferenced(bikeId);

    public IObservable<Unit> Changes { get; }

}
