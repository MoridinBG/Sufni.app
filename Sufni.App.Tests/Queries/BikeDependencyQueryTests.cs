using System.Reactive;
using NSubstitute;
using Sufni.App.Services;

using Sufni.App.Bikes.Queries;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Tests.Queries;

public class BikeDependencyQueryTests
{
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Board> boardRepository = Substitute.For<ISynchronizableRepository<Board>>();
    private readonly SetupStore setupStore;

    public BikeDependencyQueryTests()
    {
        setupStore = new SetupStore(setupRepository, boardRepository);
        boardRepository.GetAllAsync().Returns(Task.FromResult(new List<Board>()));
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
        using var query = CreateQuery();

        Assert.True(await query.IsBikeInUseAsync(bikeId));
        Assert.False(await query.IsBikeInUseAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task IsBikeInUseAsync_ToleratesNullRepositoryEntries()
    {
        setupRepository.GetAllAsync().Returns(Task.FromResult(new List<Setup> { null! }));
        using var query = CreateQuery();

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
        using var query = CreateQuery();

        Assert.False(query.IsBikeInUse(bikeId));

        await setupStore.RefreshAsync();

        Assert.True(query.IsBikeInUse(bikeId));
        Assert.False(query.IsBikeInUse(Guid.NewGuid()));
    }

    [Fact]
    public async Task Changes_EmitsInitiallyAndPerStoreChange()
    {
        setupRepository.GetAllAsync().Returns(Task.FromResult(new List<Setup>
        {
            new(Guid.NewGuid(), "race") { BikeId = Guid.NewGuid() },
        }));
        using var query = CreateQuery();
        await setupStore.RefreshAsync();

        // A subscriber arriving after items exist receives the cache's
        // current state as an initial emit, then one emit per change.
        var emits = 0;
        using var subscription = query.Changes.Subscribe(_ => emits++);
        Assert.Equal(1, emits);

        await setupStore.RefreshAsync();
        Assert.True(emits > 1);
    }
}
