using System.Reactive.Linq;
using DynamicData;
using NSubstitute;

using Sufni.App.Sessions.Services;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Async;
namespace Sufni.App.Tests.Shared.Base;

public class PersistedStoreTests
{
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();

    [Fact]
    public async Task BikeStore_RefreshLoadsSnapshots_AndCommitMutationsUpdateCache()
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
        bikeRepository.PutAsync(Arg.Any<Bike>()).Returns(callInfo =>
            Task.FromResult(callInfo.Arg<Bike>().Id));
        bikeRepository.DeleteAsync(bikeId).Returns(Task.CompletedTask);
        var store = new BikeStore(bikeRepository, UiThreadDispatcher);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(bikeId, snapshot.Id);
        Assert.Equal("Trail bike", snapshot.Name);
        Assert.Equal(snapshot, store.Get(bikeId));

        var updated = snapshot with { Name = "Enduro bike" };
        var updateResult = await store.CommitBikeAsync(Bike.FromSnapshot(updated));
        Assert.IsType<StoreMutationResult<BikeSnapshot>.Saved>(updateResult);
        Assert.Equal(updated, store.Get(bikeId));

        var deleteResult = await store.CommitBikeDeleteAsync(bikeId);
        Assert.IsType<StoreDeleteResult<BikeSnapshot>.Deleted>(deleteResult);
        Assert.Empty(snapshots);
        Assert.Null(store.Get(bikeId));
    }

    [Fact]
    public async Task RefreshAsync_DispatchesCacheReplacement_WhenOffUiThread()
    {
        var bike = new Bike
        {
            Id = Guid.NewGuid(),
            Name = "Trail bike",
            HeadAngle = 64,
            Updated = 7
        };
        var bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
        bikeRepository.GetAllAsync().Returns([bike]);
        var dispatcher = new RecordingUiThreadDispatcher(checkAccess: false);
        var store = new BikeStore(bikeRepository, dispatcher);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.Single(snapshots);
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
        var store = new SetupStore(setupRepository, boardRepository, UiThreadDispatcher);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(setupId, snapshot.Id);
        Assert.Equal(boardId, snapshot.BoardId);
        Assert.Equal(snapshot, store.Get(setupId));
        Assert.Equal(snapshot, store.FindByBoardId(boardId));
    }

    [Fact]
    public async Task SessionStore_RefreshLoadsMetadata_AndPublishMutationsUpdateCache()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Morning run", "desc", null, 100)
        {
            HasProcessedData = true,
            Updated = 11
        };
        sessionRepository.GetSessionsAsync().Returns([session]);
        var store = new SessionStore(sessionRepository, sessionTelemetryWriter, UiThreadDispatcher);
        using var snapshotsSubscription = store.Connect().Bind(out var snapshots).Subscribe();
        var watched = new List<SessionSnapshot>();
        using var watchSubscription = store.Watch(sessionId).Subscribe(watched.Add);

        await store.RefreshAsync();
        await store.PublishSessionsRemovedAsync([sessionId]);
        var updatedSession = new Session(sessionId, "Evening run", "desc", null, 100)
        {
            HasProcessedData = true,
            Updated = 12
        };
        var updated = SessionSnapshot.From(updatedSession);
        sessionRepository.GetSessionAsync(sessionId).Returns(updatedSession);
        await store.PublishSessionsChangedAsync([sessionId]);

        var committedSession = new Session(sessionId, "Night run", "desc", null, 100)
        {
            HasProcessedData = true,
            Updated = 13
        };
        sessionRepository.PutSessionAsync(committedSession).Returns(Task.FromResult(sessionId));
        sessionRepository.GetSessionAsync(sessionId).Returns(committedSession);
        var commitResult = await store.CommitSessionMetadataAsync(committedSession, updated.Updated);
        var committed = Assert.IsType<StoreMutationResult<SessionSnapshot>.Saved>(commitResult);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(committed.Snapshot, snapshot);
        Assert.Equal(committed.Snapshot, store.Get(sessionId));
        Assert.Equal(3, watched.Count);
        Assert.Equal("Morning run", watched[0].Name);
        Assert.Equal("Evening run", watched[1].Name);
        Assert.Equal("Night run", watched[2].Name);
    }

    [Fact]
    public async Task PairedDeviceStore_RefreshUsesDeviceIdKeys_AndCommitPublishMutationsUpdateCache()
    {
        var expires = DateTime.UtcNow.AddHours(1);
        var device = new PairedDevice("device-1", "Phone", expires);
        var pairedDeviceRepository = Substitute.For<IPairedDeviceRepository>();
        pairedDeviceRepository.GetPairedDevicesAsync().Returns([device]);
        pairedDeviceRepository.DeletePairedDeviceAsync("device-1").Returns(Task.CompletedTask);
        var store = new PairedDeviceStore(pairedDeviceRepository, UiThreadDispatcher);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("device-1", snapshot.DeviceId);
        Assert.Equal(snapshot, store.Get("device-1"));

        var updatedDevice = new PairedDevice("device-1", "Tablet", expires);
        pairedDeviceRepository.GetPairedDeviceAsync("device-1").Returns(updatedDevice);
        var updated = PairedDeviceSnapshot.From(updatedDevice);
        await store.PublishPairedDevicesChangedAsync(["device-1"]);
        Assert.Equal(updated, store.Get("device-1"));

        var deleteResult = await store.CommitLocalUnpairAsync("device-1");
        Assert.IsType<StoreDeleteResult<PairedDeviceSnapshot>.Deleted>(deleteResult);
        Assert.Empty(snapshots);
        Assert.Null(store.Get("device-1"));
    }
}
