using Sufni.App.Shared.Models;
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

    [Fact]
    public async Task BikeStore_ReplacesSnapshots_AndPublicationsUpdateCache()
    {
        var bikeId = Guid.NewGuid();
        var bike = new Bike
        {
            Id = bikeId,
            Name = "Trail bike",
            HeadAngle = 64,
            Updated = 7
        };
        using var publication = new StorePublication(UiThreadDispatcher);
        using var store = new BikeStore(publication);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        await store.ReplaceAllAsync([BikeSnapshot.From(bike)]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(BikeSnapshot.From(bike), snapshot);

        var updated = snapshot with { Name = "Enduro bike" };
        await store.PublishBikesChangedAsync([updated]);
        Assert.Equal(updated, Assert.Single(snapshots));

        await store.PublishBikesRemovedAsync([bikeId]);
        Assert.Empty(snapshots);
        Assert.Null(store.Get(bikeId));
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
        var dispatcher = new ManualUiThreadDispatcher();
        using var publication = new StorePublication(dispatcher);
        using var store = new BikeStore(publication);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();

        var write = store.ReplaceAllAsync([BikeSnapshot.From(bike)]);
        Assert.Empty(snapshots);
        Assert.Null(store.Get(bike.Id));
        await dispatcher.DrainAsync();
        await write.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BikeSnapshot.From(bike), Assert.Single(snapshots));
        Assert.Equal(BikeSnapshot.From(bike), store.Get(bike.Id));
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
        using var publication = new StorePublication(UiThreadDispatcher);
        using var store = new BikeStore(publication);
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
    public async Task SessionStore_ReplacesMetadata_AndPublishMutationsUpdateCache()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Morning run", "desc", null, 100)
        {
            ProcessedData = [],
            Updated = 11
        };
        using var publication = new StorePublication(UiThreadDispatcher);
        using var store = new SessionStore(publication);
        using var snapshotsSubscription = store.Connect().Bind(out var snapshots).Subscribe();
        var watched = new List<SessionSnapshot>();
        using var watchSubscription = store.Watch(sessionId).Subscribe(watched.Add);

        await store.ReplaceAllAsync([SessionSnapshot.From(session)]);
        await store.PublishSessionsRemovedAsync([sessionId], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(snapshots);
        Assert.Null(store.Get(sessionId));
        var updatedSession = new Session(sessionId, "Evening run", "desc", null, 100)
        {
            ProcessedData = [],
            Updated = 12
        };
        var updated = SessionSnapshot.From(updatedSession);
        await store.PublishSessionsChangedAsync([updated], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(updated, Assert.Single(snapshots));
        Assert.Equal(updated, store.Get(sessionId));
        Assert.Equal(["Morning run", "Evening run"], watched.Select(snapshot => snapshot.Name));
    }
}
