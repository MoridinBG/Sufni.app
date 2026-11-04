using Sufni.App.Setups.Queries;
using System.Reactive;
using System;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class BikeStoreStub : IBikeStore, IDisposable
{
    private readonly SourceCache<BikeSnapshot, Guid> cache = new(snapshot => snapshot.Id);

    public BikeStoreStub(params BikeSnapshot[] snapshots)
    {
        cache.AddOrUpdate(snapshots);
    }

    public IObservable<IChangeSet<BikeSnapshot, Guid>> Connect() => cache.Connect();

    public void Add(BikeSnapshot snapshot) => cache.AddOrUpdate(snapshot);
    public void Remove(Guid id) => cache.RemoveKey(id);

    public void Dispose() => cache.Dispose();

    public BikeSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

}

internal sealed class PairedDeviceStoreStub : IPairedDeviceStore, IDisposable
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

    public void Dispose() => cache.Dispose();

    public PairedDeviceSnapshot? Get(string deviceId)
    {
        var result = cache.Lookup(deviceId);
        return result.HasValue ? result.Value : null;
    }

}

internal sealed class SetupBoardQueryStub : ISetupBoardQuery, IDisposable
{
    private readonly SourceCache<SetupDetails, Guid> cache = new(row => row.Setup.Id);

    public SetupBoardQueryStub(params SetupDetails[] rows) => cache.AddOrUpdate(rows);
    public IObservable<IChangeSet<SetupDetails, Guid>> Connect() => cache.Connect();
    public IObservable<Unit> Changes => cache.Connect().Select(_ => Unit.Default);
    public void Add(SetupSnapshot setup, Guid? boardId = null) => cache.AddOrUpdate(new SetupDetails(setup, boardId));
    public void Remove(Guid id) => cache.RemoveKey(id);
    public SetupDetails? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }
    public SetupSnapshot? FindByBoardId(Guid boardId) => cache.Items.FirstOrDefault(row => row.BoardId == boardId)?.Setup;
    public void Dispose() => cache.Dispose();
}
