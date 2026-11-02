using Sufni.App.Setups.Queries;
using System.Reactive;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
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

    public void Dispose() => cache.Dispose();

    public BikeSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

}

internal sealed class SetupStoreStub : ISetupStore, IDisposable
{
    private readonly SourceCache<SetupSnapshot, Guid> cache = new(snapshot => snapshot.Id);

    public SetupStoreStub(params SetupSnapshot[] snapshots)
    {
        cache.AddOrUpdate(snapshots);
    }

    public IObservable<IChangeSet<SetupSnapshot, Guid>> Connect() => cache.Connect();

    public void Add(SetupSnapshot snapshot) => cache.AddOrUpdate(snapshot);

    public void Dispose() => cache.Dispose();

    public SetupSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

    public IReadOnlyList<SetupSnapshot> GetAll() => cache.Items.ToArray();

    public bool IsBikeReferenced(Guid bikeId) => cache.Items.Any(setup => setup.BikeId == bikeId);

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

internal sealed class SessionStoreStub : ISessionStore, IDisposable
{
    public IReadOnlyList<SessionSnapshot> GetAll() => cache.Items.ToArray();

    private readonly SourceCache<SessionSnapshot, Guid> cache = new(snapshot => snapshot.Id);

    public IObservable<IChangeSet<SessionSnapshot, Guid>> Connect() => cache.Connect();

    public IObservable<SessionSnapshot> Watch(Guid id) => Observable.Empty<SessionSnapshot>();

    public SessionSnapshot? Get(Guid id)
    {
        var result = cache.Lookup(id);
        return result.HasValue ? result.Value : null;
    }

    public void Add(SessionSnapshot snapshot) => cache.AddOrUpdate(snapshot);

    public void RefreshWith(SessionSnapshot snapshot) =>
        cache.EditDiff([snapshot], EqualityComparer<SessionSnapshot>.Default);

    public void Remove(Guid id) => cache.RemoveKey(id);

    public void Dispose() => cache.Dispose();
}

internal sealed class RecordedSourceStoreStub : IRecordedSessionSourceStore, IDisposable
{
    private readonly SourceCache<RecordedSessionSourceSnapshot, Guid> cache = new(snapshot => snapshot.SessionId);

    public IObservable<IChangeSet<RecordedSessionSourceSnapshot, Guid>> Connect() => cache.Connect();

    public RecordedSessionSourceSnapshot? Get(Guid sessionId)
    {
        var result = cache.Lookup(sessionId);
        return result.HasValue ? result.Value : null;
    }

    public void Add(RecordedSessionSourceSnapshot snapshot) => cache.AddOrUpdate(snapshot);

    public void Dispose() => cache.Dispose();
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
