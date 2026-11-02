using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Services;
using Sufni.App.Setups.Queries;
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
    private readonly InMemorySynchronizableRepository<Session> sessions;
    private readonly InMemorySetupTransactions transactions;
    private readonly SetupCoordinator coordinator;
    private readonly SetupBoardQuery query;
    private readonly ISetupFileService files = Substitute.For<ISetupFileService>();

    public SetupCoordinatorTests()
    {
        setupStore = new(publication);
        bikeStore = new(publication);
        boardStore = new(publication);
        query = new(setupStore, boardStore, publication);
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(9));
        setups = new(clock);
        boards = new(clock);
        sessions = new(clock);
        transactions = new(setups, new InMemorySynchronizableRepository<Bike>(clock), boards, sessions);
        coordinator = new(setupStore, bikeStore, files, new TestBikeSaveValidator(), new InlineBackgroundTaskRunner(), transactions, boards, boardStore, new InlineUiThreadDispatcher(), publication, query);
    }

    public void Dispose()
    {
        query.Dispose();
        setupStore.Dispose();
        bikeStore.Dispose();
        boardStore.Dispose();
        publication.Dispose();
    }

    private async Task SeedAsync(SetupSnapshot snapshot, Guid? boardId = null)
    {
        setups.Seed(new Setup(snapshot.Id, snapshot.Name)
        {
            BikeId = snapshot.BikeId, Updated = snapshot.Updated,
            FrontSensorConfigurationJson = snapshot.FrontSensorConfigurationJson,
            RearSensorConfigurationJson = snapshot.RearSensorConfigurationJson,
        });
        await bikeStore.PublishBikesChangedAsync([TestSnapshots.Bike(id: snapshot.BikeId)]);
        if (boardId is { } id)
        {
            var board = new Board(id, snapshot.Id);
            boards.Seed(board);
            await boardStore.PublishBoardsChangedAsync([BoardSnapshot.From(board)]);
        }
        await setupStore.PublishSetupsChangedAsync([snapshot]);
    }

    [Fact]
    public async Task ImportSetupAsync_ReturnsFileRejectionWithoutPersisting()
    {
        files.ReadAsync(Arg.Any<CancellationToken>()).Returns(new SetupFileReadResult.InvalidFile("invalid setup"));

        var result = await coordinator.ImportSetupAsync(TestContext.Current.CancellationToken);

        Assert.IsType<SetupImportResult.InvalidFile>(result);
        Assert.Empty(await setups.GetAllAsync());
    }

    [Fact]
    public async Task ImportSetupAsync_CompletesPublicationAfterReadEvenIfViewTokenIsCanceled()
    {
        var bike = new Bike(Guid.NewGuid(), "imported bike") { HeadAngle = 65, ForkStroke = 160 };
        var setup = new Setup(Guid.NewGuid(), "imported setup") { BikeId = bike.Id, FrontSensorConfigurationJson = "{\"type\":\"linear_fork\",\"length\":150,\"resolution\":12}" };
        var boardId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        files.ReadAsync(cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return new SetupFileReadResult.Read(new(setup, bike, boardId));
        });

        var result = Assert.IsType<SetupImportResult.Imported>(await coordinator.ImportSetupAsync(cancellation.Token));

        Assert.Equal(setup.Id, result.Data.Setup.Id);
        Assert.Equal(setup.Name, (await setups.GetAsync(setup.Id))!.Name);
        Assert.Equal(setup.Id, (await boards.GetAsync(boardId))!.SetupId);
        Assert.Equal(boardId, query.Get(setup.Id)!.BoardId);
        Assert.NotNull(bikeStore.Get(bike.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateDraft_ClaimedSuggestedBoardIsRejectedWhenCreatingTheSetup(bool claimed)
    {
        var boardId = Guid.NewGuid();
        var other = TestSnapshots.Setup();
        if (claimed) await SeedAsync(other, boardId);
        var bike = TestSnapshots.Bike();
        await bikeStore.PublishBikesChangedAsync([bike]);
        var created = coordinator.CreateDraft(boardId);
        var draft = SetupDraft.From(created.Setup, created.BoardId) with
        {
            BikeId = bike.Id, FrontSensor = new SensorDraft.LinearFork(150, 12),
        };

        var result = await coordinator.SaveAsync(draft, null);

        if (claimed)
        {
            Assert.Equal(boardId, Assert.IsType<SetupSaveResult.BoardInUse>(result).BoardId);
            Assert.Equal(other.Id, (await boards.GetAsync(boardId))!.SetupId);
            Assert.Null(await setups.GetAsync(draft.Id));
        }
        else
        {
            Assert.IsType<SetupSaveResult.Saved>(result);
            Assert.Equal(draft.Id, (await boards.GetAsync(boardId))!.SetupId);
        }
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
        var existing = TestSnapshots.Setup(updated: 5);
        await SeedAsync(existing, oldBoard);
        var draft = new SetupDraft(existing.Id, "renamed", existing.BikeId, newBoard, new SensorDraft.LinearFork(150, 12), null);

        var observed = new List<(string?, Guid?, Guid?)>();
        using var watch = publication.Committed.Subscribe(_ => observed.Add((setupStore.Get(existing.Id)?.Name,
            oldBoard is { } oldId ? boardStore.Get(oldId)?.SetupId : null,
            newBoard is { } newId ? boardStore.Get(newId)?.SetupId : null)));
        var result = await coordinator.SaveAsync(draft, SetupDraft.From(existing, oldBoard));
        if (!hasBoard)
        {
            Assert.Equal(SetupValidationFailureReason.Board, Assert.IsType<SetupSaveResult.Invalid>(result).Reason);
            Assert.Empty(observed);
            Assert.Equal(existing, setupStore.Get(existing.Id));
            return;
        }

        Assert.Equal(("renamed", sameBoard ? existing.Id : (Guid?)null, hasBoard ? existing.Id : (Guid?)null), Assert.Single(observed));
        Assert.Equal(9, Assert.IsType<SetupSaveResult.Saved>(result).Snapshot.Updated);
        Assert.Equal("renamed", (await setups.GetAsync(existing.Id))!.Name);
        var published = setupStore.Get(existing.Id)!;
        Assert.Equal("renamed", published.Name);
        Assert.Equal(newBoard, query.Get(existing.Id)!.BoardId);
        Assert.Equal(9, published.Updated);
        if (newBoard is { } claimed) Assert.Equal(existing.Id, (await boards.GetAsync(claimed))!.SetupId);
        if (oldBoard is { } cleared && oldBoard != newBoard) Assert.Null((await boards.GetAsync(cleared))!.SetupId);
    }

    [Fact]
    public async Task SaveAsync_ConflictPreservesPersistedAndPublishedSetup()
    {
        var current = TestSnapshots.Setup(updated: 10);
        await SeedAsync(current);

        var draft = new SetupDraft(current.Id, "stale", current.BikeId, Guid.NewGuid(), new SensorDraft.LinearFork(150, 12), null);
        var result = await coordinator.SaveAsync(draft, SetupDraft.From(current with { Name = "original" }, null));

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

        var draft = new SetupDraft(existing.Id, "edited", existing.BikeId, Guid.NewGuid(), new SensorDraft.LinearFork(150, 12), null);
        var result = await coordinator.SaveAsync(draft, SetupDraft.From(existing, null));

        Assert.Equal(SetupSaveFailureReason.Save, Assert.IsType<SetupSaveResult.Failed>(result).Reason);
        Assert.Equal(existing.Name, (await setups.GetAsync(existing.Id))!.Name);
        Assert.Equal(existing, setupStore.Get(existing.Id));
    }

    [Fact]
    public async Task SaveAsync_IgnoresTimestampAndSensorJsonFormatting_AndSerializesTheDraft()
    {
        var board = Guid.NewGuid();
        var original = TestSnapshots.Setup() with { FrontSensorConfigurationJson = "{\"type\":\"linear_fork\",\"length\":150,\"resolution\":12}" };
        var current = original with { Updated = 20, FrontSensorConfigurationJson = "{ \"type\": \"linear_fork\", \"length\": 150, \"resolution\": 12 }" };
        await SeedAsync(current, board);
        var baseline = SetupDraft.From(original, board);
        var draft = baseline with { FrontSensor = new SensorDraft.LinearFork(175, 14) };

        var result = Assert.IsType<SetupSaveResult.Saved>(await coordinator.SaveAsync(draft, baseline));

        Assert.Equal(draft, SetupDraft.From(result.Snapshot, result.BoardId));
        var savedSensor = Assert.IsType<LinearForkSensorConfiguration>(SensorConfiguration.FromJson((await setups.GetAsync(original.Id))!.FrontSensorConfigurationJson!));
        Assert.Equal(175, savedSensor.Length);
        Assert.Equal(14, savedSensor.Resolution);
    }

    [Fact]
    public async Task ImportSetupAsync_RejectsMalformedSensorPayloadWithoutPersisting()
    {
        var bike = new Bike(Guid.NewGuid(), "bike") { HeadAngle = 65, ForkStroke = 150 };
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id, FrontSensorConfigurationJson = "not json" };
        files.ReadAsync(Arg.Any<CancellationToken>()).Returns(new SetupFileReadResult.Read(new(setup, bike, Guid.NewGuid())));

        Assert.IsType<SetupImportResult.InvalidFile>(await coordinator.ImportSetupAsync(TestContext.Current.CancellationToken));

        Assert.Empty(await setups.GetAllAsync());
        Assert.Empty(await boards.GetAllAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_RejectsClaimedBoardWithoutPublishingForNewAndExistingSetups(bool isNew)
    {
        var current = TestSnapshots.Setup();
        await SeedAsync(current);
        var occupied = Guid.NewGuid();
        var other = Guid.NewGuid();
        boards.Seed(new Board(occupied, other));
        var id = isNew ? Guid.NewGuid() : current.Id;
        var draft = new SetupDraft(id, "changed", current.BikeId, occupied, new SensorDraft.LinearFork(150, 12), null);
        var changes = 0;
        using var watch = publication.Committed.Subscribe(_ => changes++);

        var result = await coordinator.SaveAsync(draft, isNew ? null : SetupDraft.From(current, null));

        Assert.Equal(occupied, Assert.IsType<SetupSaveResult.BoardInUse>(result).BoardId);
        Assert.Equal(other, (await boards.GetAsync(occupied))!.SetupId);
        Assert.Equal(0, changes);
        if (isNew) Assert.Null(await setups.GetAsync(id));
        else Assert.Equal(current.Name, (await setups.GetAsync(id))!.Name);
    }

    [Fact]
    public async Task ImportSetupAsync_RejectsClaimedBoardInsteadOfDroppingItsAssociation()
    {
        var occupied = Guid.NewGuid();
        var other = Guid.NewGuid();
        boards.Seed(new Board(occupied, other));
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(occupied, other, 1)]);
        var bike = new Bike(Guid.NewGuid(), "bike") { HeadAngle = 65, ForkStroke = 150 };
        var setup = new Setup(Guid.NewGuid(), "setup")
        {
            BikeId = bike.Id, FrontSensorConfigurationJson = "{\"type\":\"linear_fork\",\"length\":150,\"resolution\":12}",
        };
        files.ReadAsync(Arg.Any<CancellationToken>()).Returns(new SetupFileReadResult.Read(new(setup, bike, occupied)));

        var result = await coordinator.ImportSetupAsync(TestContext.Current.CancellationToken);

        Assert.Equal(occupied, Assert.IsType<SetupImportResult.BoardInUse>(result).BoardId);
        Assert.Empty(await setups.GetAllAsync());
        Assert.Null(bikeStore.Get(bike.Id));
        Assert.Equal(other, boardStore.Get(occupied)!.SetupId);
    }

    [Fact]
    public async Task DeleteAsync_InUseKeepsSetupAndBoardPublished()
    {
        var setup = TestSnapshots.Setup();
        var board = Guid.NewGuid();
        await SeedAsync(setup, board);
        sessions.Seed(new Session(Guid.NewGuid(), "ride", "", setup.Id));

        Assert.IsType<SetupDeleteResult.InUse>(await coordinator.DeleteAsync(setup.Id));

        Assert.Equal(setup, setupStore.Get(setup.Id));
        Assert.Equal(setup.Id, boardStore.Get(board)!.SetupId);
        Assert.NotNull(await setups.GetAsync(setup.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_RemovesSetupAndClearsBoard_OnlyWhenPersistenceSucceeds(bool fail)
    {
        var boardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup();
        await SeedAsync(existing, boardId);
        if (fail) transactions.Failure = new IOException("locked");

        var result = await coordinator.DeleteAsync(existing.Id);

        if (fail)
        {
            Assert.Equal(SetupDeleteFailureReason.Delete, Assert.IsType<SetupDeleteResult.Failed>(result).Reason);
            Assert.NotNull(await setups.GetAsync(existing.Id));
            Assert.Equal(existing.Id, (await boards.GetAsync(boardId))!.SetupId);
            Assert.Equal(existing, setupStore.Get(existing.Id));
        }
        else
        {
            Assert.IsType<SetupDeleteResult.Deleted>(result);
            Assert.Null(await setups.GetAsync(existing.Id));
            Assert.Null((await boards.GetAsync(boardId))!.SetupId);
            Assert.Null(setupStore.Get(existing.Id));
        }
    }
}
