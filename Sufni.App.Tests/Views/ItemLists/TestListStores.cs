using System;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Views.ItemLists;

internal sealed class BikeStoreStub : IBikeStore
{
    private readonly SourceCache<BikeSnapshot, Guid> cache = new(snapshot => snapshot.Id);

    public BikeStoreStub()
    {
    }

    public BikeStoreStub(params BikeSnapshot[] snapshots)
    {
        cache.AddOrUpdate(snapshots);
    }

    public IObservable<IChangeSet<BikeSnapshot, Guid>> Connect() => cache.Connect();

    public BikeSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

}

internal sealed class SetupStoreStub : ISetupStore
{
    private readonly SourceCache<SetupSnapshot, Guid> cache = new(snapshot => snapshot.Id);

    public SetupStoreStub()
    {
    }

    public SetupStoreStub(params SetupSnapshot[] snapshots)
    {
        cache.AddOrUpdate(snapshots);
    }

    public IObservable<IChangeSet<SetupSnapshot, Guid>> Connect() => cache.Connect();

    public SetupSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

    public SetupSnapshot? FindByBoardId(Guid boardId) => cache.Items.FirstOrDefault(snapshot => snapshot.BoardId == boardId);

}

internal sealed class PairedDeviceStoreStub : IPairedDeviceStore
{
    private readonly SourceCache<PairedDeviceSnapshot, string> cache = new(snapshot => snapshot.DeviceId);

    public PairedDeviceStoreStub()
    {
    }

    public PairedDeviceStoreStub(params PairedDeviceSnapshot[] snapshots)
    {
        cache.AddOrUpdate(snapshots);
    }

    public IObservable<IChangeSet<PairedDeviceSnapshot, string>> Connect() => cache.Connect();

    public PairedDeviceSnapshot? Get(string deviceId)
    {
        var result = cache.Lookup(deviceId);
        return result.HasValue ? result.Value : null;
    }

}
