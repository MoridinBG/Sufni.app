using SQLite;
using Sufni.App.Bikes.Models;
using Sufni.App.ExtensionHost.Contracts.Database;

using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
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
        var context = CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context);
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
        var context = CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context);
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
    public async Task DeleteSetupAsync_SoftDeletesSetupClearsBoardAndRefreshesCascade()
    {
        using var tempDatabase = new TempDatabase("setup-delete-cascade-transaction.db");
        var setupId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var migrator = CreateMigrator();
        var provider = new TestCascadeRuleProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Setup,
            "setup_transaction_cascade_row",
            "setup_id",
            ExtensionCascadeAction.SoftDelete));
        var refresh = new RecordingRefreshParticipant();
        var context = CreateConnectionContext(tempDatabase.DatabasePath, [migrator]);
        var cascade = new ExtensionCascadeService(context, [migrator], [provider], [refresh]);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var runner = new SetupPersistenceTransactionRunner(context, cascade);
        await connection.InsertAsync(new Setup(setupId, "setup") { BikeId = Guid.NewGuid(), Updated = 10 });
        await connection.InsertAsync(new Board(boardId, setupId) { Updated = 10 });
        await connection.InsertAsync(new SetupTransactionCascadeRow { Id = "extension", SetupId = setupId });

        await runner.DeleteSetupAsync(setupId, boardId, cancellationToken: TestContext.Current.CancellationToken);

        var setup = await connection.GetAsync<Setup>(setupId);
        var board = await connection.GetAsync<Board>(boardId);
        var extensionRow = await connection.GetAsync<SetupTransactionCascadeRow>("extension");
        Assert.NotNull(setup.Deleted);
        Assert.Null(board.SetupId);
        Assert.NotNull(extensionRow.Deleted);
        Assert.Equal(extensionRow.Deleted, extensionRow.Updated);
        Assert.Equal(1, refresh.RefreshCount);
    }

    [Fact]
    public async Task DeleteSetupAsync_RollsBackSetupAndBoard_WhenTransactionFails()
    {
        using var tempDatabase = new TempDatabase("setup-delete-rollback.db");
        var context = CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var cascade = new ThrowingCascadeService();
        var runner = new SetupPersistenceTransactionRunner(context, cascade);
        var setupId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        await connection.InsertAsync(new Setup(setupId, "setup") { BikeId = Guid.NewGuid(), Updated = 10 });
        await connection.InsertAsync(new Board(boardId, setupId) { Updated = 10 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.DeleteSetupAsync(setupId, boardId, cancellationToken: TestContext.Current.CancellationToken));

        var setup = await connection.GetAsync<Setup>(setupId);
        var board = await connection.GetAsync<Board>(boardId);
        Assert.Null(setup.Deleted);
        Assert.Equal(setupId, board.SetupId);
        Assert.Equal(0, cascade.RefreshCount);
    }

    private static SqliteConnectionContext CreateConnectionContext(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator>? migrators = null) =>
        new(
            databasePath,
            createAppDirectories: false,
            migrators ?? [],
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipantsProvider: () => []);

    private static TestExtensionMigrator CreateMigrator() =>
        new("test", targetVersion: 0, [typeof(SetupTransactionCascadeRow)], []);

    [Table("setup_transaction_cascade_row")]
    private sealed class SetupTransactionCascadeRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("setup_id")]
        public Guid SetupId { get; set; }

        [Column("deleted")]
        public long? Deleted { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    private sealed class TestCascadeRuleProvider(ExtensionCascadeRule rule) : IExtensionCascadeRuleProvider
    {
        public IReadOnlyList<ExtensionCascadeRule> Rules { get; } = [rule];
    }

    private sealed class RecordingRefreshParticipant : IExtensionStateRefreshParticipant
    {
        public int RefreshCount { get; private set; }

        public Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCascadeService : IExtensionCascadeService
    {
        public int RefreshCount { get; private set; }

        public bool ApplyRulesForDeletedCoreEntityInTransaction(
            SQLiteConnection connection,
            ExtensionCoreEntityKind kind,
            Guid id) =>
            throw new InvalidOperationException("Cascade failure");

        public Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }

        public Task RepairOrphansAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
