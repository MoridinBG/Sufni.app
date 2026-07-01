using System.Reactive.Linq;
using DynamicData;
using NSubstitute;

using Sufni.App.Sessions.Services;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Tests.Shared.Base;

public class PersistedStoreTests
{
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();

    [Fact]
    public async Task BikeStore_RefreshLoadsSnapshots_AndWriterMutationsUpdateCache()
    {
        var bikeId = Guid.NewGuid();
        var bike = new Bike
        {
            Id = bikeId,
            Name = "Trail bike",
            HeadAngle = 64,
            Updated = 7
        };
        var bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
        bikeRepository.GetAllAsync().Returns([bike]);
        var store = new BikeStore(bikeRepository);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(bikeId, snapshot.Id);
        Assert.Equal("Trail bike", snapshot.Name);
        Assert.Equal(snapshot, store.Get(bikeId));

        var updated = snapshot with { Name = "Enduro bike" };
        store.Upsert(updated);
        Assert.Equal(updated, store.Get(bikeId));

        store.Remove(bikeId);
        Assert.Empty(snapshots);
        Assert.Null(store.Get(bikeId));
    }

    [Fact]
    public async Task SetupStore_RefreshLoadsBoardAssociations_AndFindsByBoardId()
    {
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var setup = new Setup(setupId, "Race setup")
        {
            BikeId = bikeId,
            Updated = 9
        };
        var board = new Board(boardId, setupId);
        var setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
        var boardRepository = Substitute.For<ISynchronizableRepository<Board>>();
        setupRepository.GetAllAsync().Returns([setup]);
        boardRepository.GetAllAsync().Returns([board]);
        var store = new SetupStore(setupRepository, boardRepository);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(setupId, snapshot.Id);
        Assert.Equal(boardId, snapshot.BoardId);
        Assert.Equal(snapshot, store.Get(setupId));
        Assert.Equal(snapshot, store.FindByBoardId(boardId));
    }

    [Fact]
    public async Task SessionStore_RefreshLoadsMetadata_AndWatchIgnoresRemovals()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Morning run", "desc", null, 100)
        {
            HasProcessedData = true,
            Updated = 11
        };
        sessionRepository.GetSessionsAsync().Returns([session]);
        var store = new SessionStore(sessionRepository);
        using var snapshotsSubscription = store.Connect().Bind(out var snapshots).Subscribe();
        var watched = new List<SessionSnapshot>();
        using var watchSubscription = store.Watch(sessionId).Subscribe(watched.Add);

        await store.RefreshAsync();
        store.Remove(sessionId);
        var updated = SessionSnapshot.From(session) with { Name = "Evening run", Updated = 12 };
        store.Upsert(updated);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(updated, snapshot);
        Assert.Equal(updated, store.Get(sessionId));
        Assert.Equal(2, watched.Count);
        Assert.Equal("Morning run", watched[0].Name);
        Assert.Equal("Evening run", watched[1].Name);
    }

    [Fact]
    public async Task PairedDeviceStore_RefreshUsesDeviceIdKeys_AndWriterMutationsUpdateCache()
    {
        var expires = DateTime.UtcNow.AddHours(1);
        var device = new PairedDevice("device-1", "Phone", expires);
        var pairedDeviceRepository = Substitute.For<IPairedDeviceRepository>();
        pairedDeviceRepository.GetPairedDevicesAsync().Returns([device]);
        var store = new PairedDeviceStore(pairedDeviceRepository);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("device-1", snapshot.DeviceId);
        Assert.Equal(snapshot, store.Get("device-1"));

        var updated = snapshot with { DisplayName = "Tablet" };
        store.Upsert(updated);
        Assert.Equal(updated, store.Get("device-1"));

        store.Remove("device-1");
        Assert.Empty(snapshots);
        Assert.Null(store.Get("device-1"));
    }
}
