using System.Reactive.Linq;
using DynamicData;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Stores;

public class PersistedStoreTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();

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
        database.GetAllAsync<Bike>().Returns([bike]);
        var store = new BikeStore(database);
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
        database.GetAllAsync<Setup>().Returns([setup]);
        database.GetAllAsync<Board>().Returns([board]);
        var store = new SetupStore(database);
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
        database.GetSessionsAsync().Returns([session]);
        var store = new SessionStore(database);
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
        database.GetPairedDevicesAsync().Returns([device]);
        var store = new PairedDeviceStore(database);
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
