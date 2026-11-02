using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
using NSubstitute;

using Sufni.App.Bikes.Queries;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Tests.Bikes.Queries;

public class BikeDependencyQueryTests : IDisposable
{
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly SetupStore setupStore;

    public BikeDependencyQueryTests()
    {
        setupStore = new SetupStore(new StorePublication(new InlineUiThreadDispatcher()));
    }

    private BikeDependencyQuery CreateQuery() => new(setupRepository, setupStore);

    [Fact]
    public async Task IsBikeInUseAsync_ChecksTheRepositoryForReferencingSetups()
    {
        var bikeId = Guid.NewGuid();
        setupRepository.GetAllAsync().Returns(Task.FromResult(new List<Setup>
        {
            new(Guid.NewGuid(), "other") { BikeId = Guid.NewGuid() },
            new(Guid.NewGuid(), "match") { BikeId = bikeId },
        }));
        var query = CreateQuery();

        Assert.True(await query.IsBikeInUseAsync(bikeId));
        Assert.False(await query.IsBikeInUseAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task IsBikeInUseAsync_ToleratesNullRepositoryEntries()
    {
        setupRepository.GetAllAsync().Returns(Task.FromResult(new List<Setup> { null! }));
        var query = CreateQuery();

        Assert.False(await query.IsBikeInUseAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task IsBikeInUse_AnswersFromTheStoreCache()
    {
        var bikeId = Guid.NewGuid();
        setupRepository.GetAllAsync().Returns(Task.FromResult(new List<Setup>
        {
            new(Guid.NewGuid(), "match") { BikeId = bikeId },
        }));
        var query = CreateQuery();

        Assert.False(query.IsBikeInUse(bikeId));

        await setupStore.ReplaceAllAsync((await setupRepository.GetAllAsync()).Select(setup => SetupSnapshot.From(setup)).ToArray());

        Assert.True(query.IsBikeInUse(bikeId));
        Assert.False(query.IsBikeInUse(Guid.NewGuid()));
    }

    [Fact]
    public async Task Changes_ObserversReadCurrentReferenceAfterReassignmentAndRemoval()
    {
        var firstBike = Guid.NewGuid();
        var secondBike = Guid.NewGuid();
        var setup = TestSnapshots.Setup() with { BikeId = firstBike };
        await setupStore.PublishSetupsChangedAsync([setup]);
        var query = CreateQuery();
        var observed = new List<(bool First, bool Second)>();
        using var subscription = query.Changes.Subscribe(_ =>
            observed.Add((query.IsBikeInUse(firstBike), query.IsBikeInUse(secondBike))));

        await setupStore.PublishSetupsChangedAsync([setup with { BikeId = secondBike }]);
        await setupStore.PublishSetupsRemovedAsync([setup.Id]);

        Assert.Equal([(true, false), (false, true), (false, false)], observed);
    }

    public void Dispose() => setupStore.Dispose();
}
