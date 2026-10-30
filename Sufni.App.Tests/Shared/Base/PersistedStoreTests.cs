using System.Reactive.Linq;
using DynamicData;
using NSubstitute;

using Sufni.App.Sessions.Services;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
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
    public async Task BikeStore_ReplacesSnapshots_AndCommitMutationsUpdateCache()
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
        var store = new BikeStore(bikeRepository, new StorePublication(UiThreadDispatcher));
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.ReplaceAllAsync([BikeSnapshot.From(bike)]);

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
    public async Task BikeStore_CommitMutationsPublishUpdateAndRemoveChangesets()
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
        var store = new BikeStore(bikeRepository, new StorePublication(UiThreadDispatcher));
        var changeSets = new List<IChangeSet<BikeSnapshot, Guid>>();
        using var subscription = store.Connect().Subscribe(changeSets.Add);

        await store.ReplaceAllAsync([BikeSnapshot.From(bike)]);
        await store.CommitBikeAsync(new Bike
        {
            Id = bikeId,
            Name = "Enduro bike",
            HeadAngle = 64,
            Updated = 8
        });
        await store.CommitBikeDeleteAsync(bikeId);

        Assert.Collection(
            changeSets,
            changes => AssertChange(changes, ChangeReason.Add, bikeId),
            changes => AssertChange(changes, ChangeReason.Update, bikeId),
            changes => AssertChange(changes, ChangeReason.Remove, bikeId));
    }

    [Fact]
    public async Task ReplaceAllAsync_DispatchesCacheReplacement_WhenOffUiThread()
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
        var store = new BikeStore(bikeRepository, new StorePublication(dispatcher));
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.ReplaceAllAsync([BikeSnapshot.From(bike)]);

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.Single(snapshots);
    }

    [Fact]
    public async Task SourceCacheStoreBase_PublishSnapshotAsync_DispatchesMutation_WhenOffUiThread()
    {
        var dispatcher = new RecordingUiThreadDispatcher(checkAccess: false);
        var store = new TestSourceCacheStore(new StorePublication(dispatcher));
        var snapshot = new TestSourceCacheSnapshot(Guid.NewGuid(), "Snapshot");
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.PublishAsync(snapshot);

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.Equal(snapshot, Assert.Single(snapshots));
        Assert.Equal(snapshot, store.Get(snapshot.Id));
    }

    [Fact]
    public async Task ReplaceAllAsync_DoesNotPublishChanges_WhenSnapshotsAreUnchanged()
    {
        var bikeId = Guid.NewGuid();
        var first = new Bike
        {
            Id = bikeId,
            Name = "Trail bike",
            HeadAngle = 64,
            ImageBytes = [1, 2, 3],
            Updated = 7
        };
        var second = new Bike
        {
            Id = bikeId,
            Name = "Trail bike",
            HeadAngle = 64,
            ImageBytes = [1, 2, 3],
            Updated = 7
        };
        var bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
        bikeRepository.GetAllAsync().Returns([first], [second]);
        var store = new BikeStore(bikeRepository, new StorePublication(UiThreadDispatcher));
        var changeSets = new List<IChangeSet<BikeSnapshot, Guid>>();
        using var subscription = store.Connect().Subscribe(changeSets.Add);

        await store.ReplaceAllAsync([BikeSnapshot.From(first)]);
        await store.ReplaceAllAsync([BikeSnapshot.From(second)]);

        var changeSet = Assert.Single(changeSets);
        var change = Assert.Single(changeSet);
        Assert.Equal(ChangeReason.Add, change.Reason);
        Assert.Equal(bikeId, change.Key);
    }

    [Fact]
    public async Task SetupStore_ReplacesBoardAssociations_AndFindsByBoardId()
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
        var store = new SetupStore(setupRepository, boardRepository, new StorePublication(UiThreadDispatcher));
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.ReplaceAllAsync([SetupSnapshot.From(setup, boardId)]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(setupId, snapshot.Id);
        Assert.Equal(boardId, snapshot.BoardId);
        Assert.Equal(snapshot, store.Get(setupId));
        Assert.Equal(snapshot, store.FindByBoardId(boardId));
    }

    [Fact]
    public async Task SessionStore_ReplacesMetadata_AndPublishMutationsUpdateCache()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Morning run", "desc", null, 100)
        {
            HasProcessedData = true,
            Updated = 11
        };
        sessionRepository.GetSessionsAsync().Returns([session]);
        var store = new SessionStore(sessionRepository, sessionTelemetryWriter, new StorePublication(UiThreadDispatcher));
        using var snapshotsSubscription = store.Connect().Bind(out var snapshots).Subscribe();
        var watched = new List<SessionSnapshot>();
        using var watchSubscription = store.Watch(sessionId).Subscribe(watched.Add);

        await store.ReplaceAllAsync([SessionSnapshot.From(session)]);
        await store.PublishSessionsRemovedAsync([sessionId], cancellationToken: TestContext.Current.CancellationToken);
        var updatedSession = new Session(sessionId, "Evening run", "desc", null, 100)
        {
            HasProcessedData = true,
            Updated = 12
        };
        var updated = SessionSnapshot.From(updatedSession);
        sessionRepository.GetSessionAsync(sessionId).Returns(updatedSession);
        await store.PublishSessionsChangedAsync([sessionId], cancellationToken: TestContext.Current.CancellationToken);

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
    public async Task PairedDeviceStore_ReplacementUsesDeviceIdKeys_AndCommitPublishMutationsUpdateCache()
    {
        var expires = DateTime.UtcNow.AddHours(1);
        var device = new PairedDevice("device-1", "Phone", expires);
        var pairedDeviceRepository = Substitute.For<IPairedDeviceRepository>();
        pairedDeviceRepository.GetPairedDevicesAsync().Returns([device]);
        pairedDeviceRepository.DeletePairedDeviceAsync("device-1").Returns(Task.CompletedTask);
        var store = new PairedDeviceStore(pairedDeviceRepository, new StorePublication(UiThreadDispatcher));
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.ReplaceAllAsync([PairedDeviceSnapshot.From(device)]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("device-1", snapshot.DeviceId);
        Assert.Equal(snapshot, store.Get("device-1"));

        var updatedDevice = new PairedDevice("device-1", "Tablet", expires);
        pairedDeviceRepository.GetPairedDeviceAsync("device-1").Returns(updatedDevice);
        var updated = PairedDeviceSnapshot.From(updatedDevice);
        await store.PublishPairedDevicesChangedAsync(["device-1"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(updated, store.Get("device-1"));

        var deleteResult = await store.CommitLocalUnpairAsync("device-1");
        Assert.IsType<StoreDeleteResult<PairedDeviceSnapshot>.Deleted>(deleteResult);
        Assert.Empty(snapshots);
        Assert.Null(store.Get("device-1"));
    }

    [Fact]
    public async Task PublicationBatch_JoinedObserverSeesBothEditsAndRemoval_AfterStoreNotifications()
    {
        using var publication = new StorePublication(UiThreadDispatcher);
        using var left = new TestSourceCacheStore(publication);
        using var right = new TestSourceCacheStore(publication);
        var leftId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var rightId = Guid.NewGuid();
        await left.PublishAsync(new(leftId, "old left"));
        await left.PublishAsync(new(removedId, "removed"));
        await right.PublishAsync(new(rightId, "old right"));
        var pairs = new List<(string? Left, string? Removed, string? Right)>();
        var joined = new List<(string? Left, string? Removed, string? Right)>();
        var visibleAtCommit = new List<(string? Left, string? Removed, string? Right)>();
        using var combined = left.Connect()
            .Select(_ => (Name: left.Get(leftId)?.Name, Removed: left.Get(removedId)?.Name))
            .CombineLatest(right.Connect().Select(_ => right.Get(rightId)?.Name),
                (leftValues, rightValue) => (leftValues.Name, leftValues.Removed, rightValue))
            .Subscribe(pairs.Add);
        using var committed = publication.Committed
            .Where(commit => commit.Stores.Contains(left) || commit.Stores.Contains(right))
            .Subscribe(_ =>
            {
                joined.Add((left.Get(leftId)?.Name, left.Get(removedId)?.Name, right.Get(rightId)?.Name));
                visibleAtCommit.Add(pairs.Last());
            });

        using (publication.BeginBatch())
        {
            await left.PublishAsync(new(leftId, "new left"));
            await left.RemoveAsync(removedId);
            await right.PublishAsync(new(rightId, "new right"));
            Assert.Empty(joined);
            Assert.Single(pairs);
        }

        Assert.Equal(("new left", null, "new right"), Assert.Single(joined));
        Assert.Equal(joined, visibleAtCommit);
        Assert.Equal(
            [("old left", "removed", "old right"), ("new left", null, "old right"), ("new left", null, "new right")],
            pairs);
    }

    [Fact]
    public async Task PublicationCommit_IdentifiesChangedStore_AndSkipsEmptyRefreshBatch()
    {
        using var publication = new StorePublication(UiThreadDispatcher);
        using var store = new TestSourceCacheStore(publication);
        using var other = new TestSourceCacheStore(publication);
        var snapshot = new TestSourceCacheSnapshot(Guid.NewGuid(), "Recorded");
        var observed = new List<string?>();
        using var subscription = publication.Committed.Subscribe(commit =>
        {
            Assert.Contains(store, commit.Stores);
            Assert.DoesNotContain(other, commit.Stores);
            observed.Add(store.Get(snapshot.Id)?.Name);
        });

        await store.PublishAsync(snapshot);
        using (publication.BeginBatch())
        {
            await store.ReplaceAsync([snapshot]);
            await other.RemoveAsync(Guid.NewGuid());
        }
        await store.RemoveAsync(snapshot.Id);

        Assert.Equal(["Recorded", null], observed);
    }

    private sealed record TestSourceCacheSnapshot(Guid Id, string Name);

    private sealed class TestSourceCacheStore(StorePublication publication)
        : SourceCacheStoreBase<TestSourceCacheSnapshot, Guid>(snapshot => snapshot.Id, publication)
    {
        public Task PublishAsync(TestSourceCacheSnapshot snapshot) =>
            PublishSnapshotAsync(snapshot);

        public Task RemoveAsync(Guid id) => PublishRemoveAsync(id);

        public Task ReplaceAsync(IReadOnlyList<TestSourceCacheSnapshot> snapshots) => ReplaceAllAsync(snapshots);
    }

    private static void AssertChange(
        IChangeSet<BikeSnapshot, Guid> changes,
        ChangeReason reason,
        Guid key)
    {
        var change = Assert.Single(changes);
        Assert.Equal(reason, change.Reason);
        Assert.Equal(key, change.Key);
    }
}
