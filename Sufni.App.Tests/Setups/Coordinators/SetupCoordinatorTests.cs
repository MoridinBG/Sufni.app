using Sufni.App.SyncAndPairing.Stores;
using System.Reactive.Linq;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Setups.Coordinators;

public class SetupCoordinatorTests : IDisposable
{
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly SetupStore setupStore;
    private readonly BikeStore bikeStore;
    private readonly BoardStore boardStore;
    private readonly InMemorySynchronizableRepository<Setup> setups;
    private readonly InMemorySynchronizableRepository<Board> boards;
    private readonly InMemorySetupTransactions transactions;
    private readonly SetupCoordinator coordinator;
    private readonly IFilesService files = Substitute.For<IFilesService>();

    public SetupCoordinatorTests()
    {
        setupStore = new(publication);
        bikeStore = new(publication);
        boardStore = new(publication);
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(9));
        setups = new(clock);
        boards = new(clock);
        transactions = new(setups, new InMemorySynchronizableRepository<Bike>(clock), boards);
        coordinator = new(setupStore, bikeStore, files, new InlineBackgroundTaskRunner(), transactions, boards, boardStore, new InlineUiThreadDispatcher(), publication);
    }

    public void Dispose()
    {
        setupStore.Dispose();
        bikeStore.Dispose();
        boardStore.Dispose();
        publication.Dispose();
    }

    private async Task SeedAsync(SetupSnapshot snapshot)
    {
        setups.Seed(new Setup(snapshot.Id, snapshot.Name) { BikeId = snapshot.BikeId, Updated = snapshot.Updated });
        if (snapshot.BoardId is { } boardId)
        {
            var board = new Board(boardId, snapshot.Id);
            boards.Seed(board);
            await boardStore.PublishBoardsChangedAsync([BoardSnapshot.From(board)]);
        }
        await setupStore.PublishSetupsChangedAsync([snapshot]);
    }

    [Fact]
    public async Task ImportSetupAsync_ReturnsInvalidFile_WhenSelectedJsonIsMalformed()
    {
        var file = TestStorageItems.TextFile("setup.json", "not json");
        files.OpenSetupFileAsync().Returns(file);

        var result = await coordinator.ImportSetupAsync(TestContext.Current.CancellationToken);

        Assert.IsType<SetupImportResult.InvalidFile>(result);
        Assert.Empty(await setups.GetAllAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateDraft_UsesSuggestedBoardOnlyWhenUnclaimed(bool claimed)
    {
        var boardId = Guid.NewGuid();
        if (claimed) await SeedAsync(TestSnapshots.Setup(boardId: boardId));

        var draft = coordinator.CreateDraft(boardId);

        Assert.Equal(claimed ? null : (Guid?)boardId, draft.BoardId);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveAsync_PersistsSetupAndBoardAssociation_ThenPublishes(bool hadBoard, bool hasBoard, bool sameBoard)
    {
        Guid? oldBoard = hadBoard ? Guid.NewGuid() : null;
        Guid? newBoard = hasBoard ? sameBoard ? oldBoard : Guid.NewGuid() : null;
        var existing = TestSnapshots.Setup(boardId: oldBoard, updated: 5);
        await SeedAsync(existing);
        var draft = new Setup(existing.Id, "renamed") { BikeId = existing.BikeId };

        var observed = new List<(string?, Guid?, Guid?)>();
        using var watch = publication.Committed.Subscribe(_ => observed.Add((setupStore.Get(existing.Id)?.Name,
            oldBoard is { } oldId ? boardStore.Get(oldId)?.SetupId : null,
            newBoard is { } newId ? boardStore.Get(newId)?.SetupId : null)));
        var result = await coordinator.SaveAsync(draft, newBoard, 5);

        Assert.Equal(("renamed", sameBoard ? existing.Id : (Guid?)null, hasBoard ? existing.Id : (Guid?)null), Assert.Single(observed));
        Assert.Equal(9, Assert.IsType<SetupSaveResult.Saved>(result).NewBaselineUpdated);
        Assert.Equal("renamed", (await setups.GetAsync(existing.Id))!.Name);
        var published = setupStore.Get(existing.Id)!;
        Assert.Equal("renamed", published.Name);
        Assert.Equal(newBoard, published.BoardId);
        Assert.Equal(9, published.Updated);
        if (newBoard is { } claimed) Assert.Equal(existing.Id, (await boards.GetAsync(claimed))!.SetupId);
        if (oldBoard is { } cleared && oldBoard != newBoard) Assert.Null((await boards.GetAsync(cleared))!.SetupId);
    }

    [Fact]
    public async Task SaveAsync_ConflictPreservesPersistedAndPublishedSetup()
    {
        var current = TestSnapshots.Setup(updated: 10);
        await SeedAsync(current);

        var result = await coordinator.SaveAsync(new Setup(current.Id, "stale"), null, 5);

        Assert.Equal(current, Assert.IsType<SetupSaveResult.Conflict>(result).CurrentSnapshot);
        Assert.Equal(current.Name, (await setups.GetAsync(current.Id))!.Name);
        Assert.Equal(current, setupStore.Get(current.Id));
    }

    [Fact]
    public async Task SaveAsync_FailurePreservesPublishedSetup()
    {
        var existing = TestSnapshots.Setup(updated: 5);
        await SeedAsync(existing);
        transactions.Failure = new IOException("disk full");

        var result = await coordinator.SaveAsync(new Setup(existing.Id, "edited"), null, 5);

        Assert.Equal(SetupSaveFailureReason.Save, Assert.IsType<SetupSaveResult.Failed>(result).Reason);
        Assert.Equal(existing.Name, (await setups.GetAsync(existing.Id))!.Name);
        Assert.Equal(existing, setupStore.Get(existing.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_RemovesSetupAndClearsBoard_OnlyWhenPersistenceSucceeds(bool fail)
    {
        var existing = TestSnapshots.Setup(boardId: Guid.NewGuid());
        await SeedAsync(existing);
        if (fail) transactions.Failure = new IOException("locked");

        var result = await coordinator.DeleteAsync(existing.Id);

        if (fail)
        {
            Assert.Equal(SetupDeleteFailureReason.Delete, Assert.IsType<SetupDeleteResult.Failed>(result).Reason);
            Assert.NotNull(await setups.GetAsync(existing.Id));
            Assert.Equal(existing.Id, (await boards.GetAsync(existing.BoardId!.Value))!.SetupId);
            Assert.Equal(existing, setupStore.Get(existing.Id));
        }
        else
        {
            Assert.IsType<SetupDeleteResult.Deleted>(result);
            Assert.Null(await setups.GetAsync(existing.Id));
            Assert.Null((await boards.GetAsync(existing.BoardId!.Value))!.SetupId);
            Assert.Null(setupStore.Get(existing.Id));
        }
    }
}
