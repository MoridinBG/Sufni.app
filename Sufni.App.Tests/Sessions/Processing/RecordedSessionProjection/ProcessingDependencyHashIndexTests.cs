using System.Reactive.Linq;
using DynamicData;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class ProcessingDependencyHashIndexTests
{
    [Fact]
    public void SetupUpdate_RecomputesOnlyThatSetup()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var firstBike = TestSnapshots.Bike(id: Guid.NewGuid());
        var secondBike = TestSnapshots.Bike(id: Guid.NewGuid());
        var firstSetup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: firstBike.Id);
        var secondSetup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: secondBike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(firstBike);
        stores.Bikes.Add(secondBike);
        stores.Setups.Add(firstSetup);
        stores.Setups.Add(secondSetup);
        changes.Clear();
        var changedSetup = firstSetup with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };

        stores.Setups.Add(changedSetup);

        var change = Assert.Single(changes);
        Assert.Equal(firstSetup.Id, change.SetupId);
        Assert.NotEqual(change.PreviousHash, change.CurrentHash);
        Assert.Equal(ProcessingDependencyHash.Compute(changedSetup, firstBike), index.GetForSetup(firstSetup.Id));
        Assert.Equal(ProcessingDependencyHash.Compute(secondSetup, secondBike), index.GetForSetup(secondSetup.Id));
    }

    [Fact]
    public void BikeUpdate_RecomputesOnlyDependentSetups()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var unrelatedBike = TestSnapshots.Bike(id: Guid.NewGuid());
        var firstSetup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var secondSetup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var unrelatedSetup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: unrelatedBike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(bike);
        stores.Bikes.Add(unrelatedBike);
        stores.Setups.Add(firstSetup);
        stores.Setups.Add(secondSetup);
        stores.Setups.Add(unrelatedSetup);
        changes.Clear();

        stores.Bikes.Add(bike with { HeadAngle = bike.HeadAngle + 1 });

        var expectedSetupIds = new[] { firstSetup.Id, secondSetup.Id }.OrderBy(id => id).ToArray();
        Assert.Equal(expectedSetupIds, changes.Select(change => change.SetupId).OrderBy(id => id).ToArray());
        Assert.DoesNotContain(changes, change => change.SetupId == unrelatedSetup.Id);
    }

    [Fact]
    public void BikeUpdate_DoesNotEmit_WhenOnlyNonProcessingFieldsChange()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(bike);
        stores.Setups.Add(setup);
        var originalHash = index.GetForSetup(setup.Id);
        changes.Clear();

        stores.Bikes.Add(bike with
        {
            Name = "renamed bike",
            ImageBytes = [9, 8, 7],
            Updated = bike.Updated + 1
        });

        Assert.Empty(changes);
        Assert.Equal(originalHash, index.GetForSetup(setup.Id));
    }

    [Fact]
    public void SetupReplaceAll_DoesNotEmit_WhenProcessingInputsAreUnchanged()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(bike);
        stores.Setups.Add(setup);
        var originalHash = index.GetForSetup(setup.Id);
        changes.Clear();

        var refreshedSetup = setup with
        {
            Name = "renamed setup",
            BoardId = Guid.NewGuid(),
            Updated = setup.Updated + 1
        };
        stores.Setups.ReplaceAll([refreshedSetup]);

        Assert.Empty(changes);
        Assert.Equal(originalHash, index.GetForSetup(setup.Id));
    }

    [Fact]
    public void BikeReplaceAll_DoesNotEmit_WhenProcessingInputsAreUnchanged()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(bike);
        stores.Setups.Add(setup);
        var originalHash = index.GetForSetup(setup.Id);
        changes.Clear();

        stores.Bikes.ReplaceAll([
            bike with
            {
                Name = "renamed bike",
                ImageBytes = [9, 8, 7],
                Updated = bike.Updated + 1
            }
        ]);

        Assert.Empty(changes);
        Assert.Equal(originalHash, index.GetForSetup(setup.Id));
    }

    [Fact]
    public void BikeRemove_ChangesDependentSetupHashesToNull()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(bike);
        stores.Setups.Add(setup);
        changes.Clear();

        stores.Bikes.Remove(bike.Id);

        var change = Assert.Single(changes);
        Assert.Equal(setup.Id, change.SetupId);
        Assert.NotNull(change.PreviousHash);
        Assert.Null(change.CurrentHash);
        Assert.Null(index.GetForSetup(setup.Id));
    }

    [Fact]
    public void SetupRemove_ChangesSetupHashToNull()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var changes = new List<ProcessingDependencyHashChange>();
        using var subscription = index.Connect().Subscribe(changes.Add);
        stores.Bikes.Add(bike);
        stores.Setups.Add(setup);
        changes.Clear();

        stores.Setups.Remove(setup.Id);

        var change = Assert.Single(changes);
        Assert.Equal(setup.Id, change.SetupId);
        Assert.NotNull(change.PreviousHash);
        Assert.Null(change.CurrentHash);
        Assert.Null(index.GetForSetup(setup.Id));
    }

    [Fact]
    public void Dispose_CompletesObservable_AndIgnoresLaterStoreChanges()
    {
        using var stores = new StoreFixtures();
        using var index = new ProcessingDependencyHashIndex(stores.Setups, stores.Bikes);
        var changes = new List<ProcessingDependencyHashChange>();
        var completed = false;
        using var subscription = index.Connect().Subscribe(
            changes.Add,
            () => completed = true);

        index.Dispose();
        stores.Bikes.Add(TestSnapshots.Bike(id: Guid.NewGuid()));

        Assert.True(completed);
        Assert.Empty(changes);
    }

    private sealed class StoreFixtures : IDisposable
    {
        public InMemorySetupStore Setups { get; } = new();
        public InMemoryBikeStore Bikes { get; } = new();

        public void Dispose()
        {
            Setups.Dispose();
            Bikes.Dispose();
        }
    }

    private sealed class InMemorySetupStore : ISetupStore, IDisposable
    {
        private readonly SourceCache<SetupSnapshot, Guid> cache = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<SetupSnapshot, Guid>> Connect() => cache.Connect();

        public SetupSnapshot? Get(Guid id)
        {
            var result = cache.Lookup(id);
            return result.HasValue ? result.Value : null;
        }

        public SetupSnapshot? FindByBoardId(Guid boardId) =>
            cache.Items.FirstOrDefault(snapshot => snapshot.BoardId == boardId);

        public void Add(SetupSnapshot snapshot) => cache.AddOrUpdate(snapshot);

        public void Remove(Guid id) => cache.RemoveKey(id);

        public void ReplaceAll(IEnumerable<SetupSnapshot> snapshots)
        {
            cache.Edit(updater =>
            {
                updater.Clear();
                updater.AddOrUpdate(snapshots);
            });
        }

        public void Dispose() => cache.Dispose();
    }

    private sealed class InMemoryBikeStore : IBikeStore, IDisposable
    {
        private readonly SourceCache<BikeSnapshot, Guid> cache = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<BikeSnapshot, Guid>> Connect() => cache.Connect();

        public BikeSnapshot? Get(Guid id)
        {
            var result = cache.Lookup(id);
            return result.HasValue ? result.Value : null;
        }

        public void Add(BikeSnapshot snapshot) => cache.AddOrUpdate(snapshot);

        public void Remove(Guid id) => cache.RemoveKey(id);

        public void ReplaceAll(IEnumerable<BikeSnapshot> snapshots)
        {
            cache.Edit(updater =>
            {
                updater.Clear();
                updater.AddOrUpdate(snapshots);
            });
        }

        public void Dispose() => cache.Dispose();
    }
}
