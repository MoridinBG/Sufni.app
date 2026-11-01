using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Setups.Coordinators;

public class SetupPersistenceTransactionRunnerTests
{
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
