using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
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

    public Task RefreshAsync() => Task.CompletedTask;
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

    public Task RefreshAsync() => Task.CompletedTask;
}

internal sealed class SessionStoreStub : ISessionStore
{
    private readonly SourceCache<SessionSnapshot, Guid> cache = new(snapshot => snapshot.Id);

    public SessionStoreStub()
    {
    }

    public SessionStoreStub(params SessionSnapshot[] snapshots)
    {
        cache.AddOrUpdate(snapshots);
    }

    public IObservable<IChangeSet<SessionSnapshot, Guid>> Connect() => cache.Connect();

    public IObservable<SessionSnapshot> Watch(Guid id) => Observable.Empty<SessionSnapshot>();

    public SessionSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

    public Task RefreshAsync() => Task.CompletedTask;
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

    public Task RefreshAsync() => Task.CompletedTask;
}