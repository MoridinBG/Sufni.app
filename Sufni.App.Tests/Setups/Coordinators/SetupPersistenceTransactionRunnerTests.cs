using Sufni.App.Sessions.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Setups.Coordinators;

public class SetupPersistenceTransactionRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedBoard_RejectsSaveOrImportWithoutWritingRows(bool import)
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"setup-claim-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "new bike");
        var setup = new Setup(Guid.NewGuid(), "changed") { BikeId = bike.Id };
        var board = new Board(Guid.NewGuid(), Guid.NewGuid()) { Version = 7, Updated = 20 };
        await connection.InsertAsync(board);
        if (!import)
        {
            await connection.InsertAsync(bike);
            await connection.InsertAsync(new Setup(setup.Id, "original") { BikeId = bike.Id });
        }

        var result = import
            ? await runner.ImportSetupAsync(bike, setup, board.Id, TestContext.Current.CancellationToken)
            : await runner.SaveSetupAsync(setup, board.Id, board.Id, TestContext.Current.CancellationToken);

        Assert.Equal(board.Id, Assert.IsType<SetupWriteResult.BoardInUse>(result).BoardId);
        var retained = await connection.GetAsync<Board>(board.Id);
        Assert.Equal(board.SetupId, retained.SetupId);
        Assert.Equal(7, retained.Version);
        Assert.Equal(20, retained.Updated);
        if (import) Assert.Null(await connection.FindAsync<Bike>(bike.Id));
        if (import) Assert.Null(await connection.FindAsync<Setup>(setup.Id));
        else Assert.Equal("original", (await connection.GetAsync<Setup>(setup.Id)).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalePreviousBoard_SaveOrDeleteNeverClearsAnotherSetupOwner(bool delete)
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"setup-stale-board-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "bike");
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id };
        var oldBoard = new Board(Guid.NewGuid(), Guid.NewGuid()) { Version = 7 };
        var next = Guid.NewGuid();
        await connection.InsertAsync(bike);
        await connection.InsertAsync(setup);
        await connection.InsertAsync(oldBoard);

        if (delete)
        {
            var result = Assert.IsType<SetupDeletePersistenceResult.Deleted>(await runner.DeleteSetupAsync(setup.Id, oldBoard.Id, TestContext.Current.CancellationToken));
            Assert.Empty(result.ChangedBoardIds);
            Assert.NotNull((await connection.GetAsync<Setup>(setup.Id)).Deleted);
        }
        else
        {
            var result = Assert.IsType<SetupWriteResult.Applied>(await runner.SaveSetupAsync(setup, oldBoard.Id, next, TestContext.Current.CancellationToken));
            Assert.Equal(next, Assert.Single(result.ChangedBoardIds));
            Assert.Equal(setup.Id, (await connection.GetAsync<Board>(next)).SetupId);
        }
        var retained = await connection.GetAsync<Board>(oldBoard.Id);
        Assert.Equal(oldBoard.SetupId, retained.SetupId);
        Assert.Equal(7, retained.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteSetup_OnlyActiveSessionReferencesBlockDeletion(bool sessionDeleted)
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"setup-in-use-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = Guid.NewGuid() };
        var board = new Board(Guid.NewGuid(), setup.Id);
        var session = new Session(Guid.NewGuid(), "ride", "", setup.Id) { Deleted = sessionDeleted ? 1 : null };
        await connection.InsertAsync(setup);
        await connection.InsertAsync(board);
        await connection.InsertAsync(session);

        var result = await runner.DeleteSetupAsync(setup.Id, board.Id, TestContext.Current.CancellationToken);

        var storedSetup = await connection.GetAsync<Setup>(setup.Id);
        var storedBoard = await connection.GetAsync<Board>(board.Id);
        if (sessionDeleted)
        {
            Assert.IsType<SetupDeletePersistenceResult.Deleted>(result);
            Assert.NotNull(storedSetup.Deleted);
            Assert.Null(storedBoard.SetupId);
        }
        else
        {
            Assert.IsType<SetupDeletePersistenceResult.InUse>(result);
            Assert.Null(storedSetup.Deleted);
            Assert.Equal(setup.Id, storedBoard.SetupId);
        }
        Assert.Equal(setup.Id, (await connection.GetAsync<Session>(session.Id)).Setup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveSetup_ReturnsBikeMissing_WithoutWritingRows_WhenBikeIsAbsentOrDeleted(bool deleted)
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"setup-bike-missing-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "bike") { Deleted = 1 };
        if (deleted) await connection.InsertAsync(bike);
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id };
        var board = Guid.NewGuid();

        var result = await runner.SaveSetupAsync(setup, null, board, TestContext.Current.CancellationToken);

        Assert.IsType<SetupWriteResult.BikeMissing>(result);
        Assert.Null(await connection.FindAsync<Setup>(setup.Id));
        Assert.Null(await connection.FindAsync<Board>(board));
    }

    [Fact]
    public async Task SaveSetup_AlreadyOwnedBoardDoesNotAdvanceItsVersion()
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"setup-same-board-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "bike");
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id };
        var board = new Board(Guid.NewGuid(), setup.Id) { Version = 7, Updated = 20 };
        await connection.InsertAsync(bike);
        await connection.InsertAsync(board);

        var result = Assert.IsType<SetupWriteResult.Applied>(await runner.SaveSetupAsync(setup, null, board.Id, TestContext.Current.CancellationToken));

        Assert.Empty(result.ChangedBoardIds);
        Assert.Equal(7, (await connection.GetAsync<Board>(board.Id)).Version);
        Assert.Equal(setup.Id, (await connection.GetAsync<Setup>(setup.Id)).Id);
    }

    [Fact]
    public async Task SaveSetupAsync_PersistsSetupAndBoardReassignment()
    {
        using var tempDatabase = new TempDatabase("setup-save-transaction.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var setupId = Guid.NewGuid();
        var oldBoardId = Guid.NewGuid();
        var newBoardId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        await connection.InsertAsync(new Bike(bikeId, "bike"));
        await connection.InsertAsync(new Board(oldBoardId, setupId) { Updated = 10 });

        await runner.SaveSetupAsync(
            new Setup(setupId, "trail setup") { BikeId = bikeId },
            oldBoardId,
            newBoardId, cancellationToken: TestContext.Current.CancellationToken);

        var setup = await connection.GetAsync<Setup>(setupId);
        var oldBoard = await connection.GetAsync<Board>(oldBoardId);
        var newBoard = await connection.GetAsync<Board>(newBoardId);
        Assert.Equal("trail setup", setup.Name);
        Assert.Equal(bikeId, setup.BikeId);
        Assert.Null(setup.Deleted);
        Assert.Null(oldBoard.SetupId);
        Assert.Equal(setupId, newBoard.SetupId);
    }

    [Fact]
    public async Task ImportSetupAsync_PersistsBikeSetupAndBoardReassignment()
    {
        using var tempDatabase = new TempDatabase("setup-import-transaction.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "bike");
        var setup = new Setup(Guid.NewGuid(), "imported setup") { BikeId = bike.Id };
        var boardId = Guid.NewGuid();

        await runner.ImportSetupAsync(bike, setup, boardId, cancellationToken: TestContext.Current.CancellationToken);

        var persistedBike = await connection.GetAsync<Bike>(bike.Id);
        var persistedSetup = await connection.GetAsync<Setup>(setup.Id);
        var board = await connection.GetAsync<Board>(boardId);
        Assert.Equal("bike", persistedBike.Name);
        Assert.Equal("imported setup", persistedSetup.Name);
        Assert.Equal(bike.Id, persistedSetup.BikeId);
        Assert.Equal(setup.Id, board.SetupId);
    }

    [Fact]
    public async Task DeleteSetupAsync_SoftDeletesSetupAndClearsBoard()
    {
        using var tempDatabase = new TempDatabase("setup-delete-transaction.db");
        var setupId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        await connection.InsertAsync(new Setup(setupId, "setup") { BikeId = Guid.NewGuid(), Updated = 10 });
        await connection.InsertAsync(new Board(boardId, setupId) { Updated = 10 });

        await runner.DeleteSetupAsync(setupId, boardId, cancellationToken: TestContext.Current.CancellationToken);

        var setup = await connection.GetAsync<Setup>(setupId);
        var board = await connection.GetAsync<Board>(boardId);
        Assert.NotNull(setup.Deleted);
        Assert.Null(board.SetupId);
    }
}
