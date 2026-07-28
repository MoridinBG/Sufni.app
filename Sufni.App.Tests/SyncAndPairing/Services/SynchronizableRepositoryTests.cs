using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizableRepositoryTests
{
    [Fact]
    public async Task PutAsync_ReusesSoftDeletedBoardRow_InsteadOfInserting()
    {
        using var tempDatabase = new TempDatabase("board-revive.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var revivedSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, null)
            {
                Updated = 10,
                ClientUpdated = 10,
                Deleted = 10
            });
        }

        await database.PutAsync(new Board(boardId, revivedSetupId));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var boards = verificationConnection.Table<Board>().Where(b => b.Id == boardId).ToList();

        var board = Assert.Single(boards);
        Assert.Equal(revivedSetupId, board.SetupId);
        Assert.Null(board.Deleted);

    }

    [Fact]
    public async Task DeleteAsync_DoesNotRestampExistingTombstone()
    {
        using var tempDatabase = new TempDatabase("delete-tombstone.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, null)
            {
                Updated = 10,
                ClientUpdated = 10,
                Deleted = 100
            });
        }

        await database.DeleteAsync(new Board(boardId, null));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(100, board.Deleted);

    }

    [Theory]
    [InlineData(RepositoryCascadeDeleteState.Live)]
    [InlineData(RepositoryCascadeDeleteState.Tombstoned)]
    [InlineData(RepositoryCascadeDeleteState.Missing)]
    public async Task DeleteAsync_AppliesCascadeRulesForCoreRowState(RepositoryCascadeDeleteState state)
    {
        using var tempDatabase = new TempDatabase("delete-cascade.db");
        var sessionId = Guid.NewGuid();
        var (context, cascade, refresh) = CreateCascadeHarness(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (state != RepositoryCascadeDeleteState.Missing)
        {
            await connection.InsertAsync(new Session(sessionId, "session", "desc", null)
            {
                Updated = 10,
                Deleted = state == RepositoryCascadeDeleteState.Tombstoned ? 100 : null
            });
        }

        await connection.InsertAsync(new RepositoryCascadeRow { Id = "extension", SessionId = sessionId });
        var repository = new SynchronizableRepository<Session>(context, cascade);

        await repository.DeleteAsync(sessionId);

        var extensionRow = await connection.GetAsync<RepositoryCascadeRow>("extension");
        Assert.NotNull(extensionRow.Deleted);
        if (state == RepositoryCascadeDeleteState.Live)
        {
            var session = await connection.GetAsync<Session>(sessionId);
            Assert.NotNull(session.Deleted);
            Assert.Equal(extensionRow.Deleted, extensionRow.Updated);
        }
        else if (state == RepositoryCascadeDeleteState.Tombstoned)
        {
            var session = await connection.GetAsync<Session>(sessionId);
            Assert.Equal(100, session.Deleted);
        }

        Assert.Equal(1, refresh.RefreshCount);
    }

    private static (SqliteConnectionContext Context, ExtensionCascadeService Cascade, RecordingRefreshParticipant Refresh)
        CreateCascadeHarness(string databasePath)
    {
        var migrator = new TestExtensionMigrator("test", targetVersion: 0, [typeof(RepositoryCascadeRow)], []);
        var provider = new TestCascadeRuleProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Session,
            "repository_cascade_row",
            "session_id",
            ExtensionCascadeAction.SoftDelete));
        var refresh = new RecordingRefreshParticipant();
        var context = PersistenceTestData.CreateConnectionContext(databasePath, [migrator]);
        var cascade = new ExtensionCascadeService(context, [migrator], [provider], [refresh]);
        return (context, cascade, refresh);
    }

    [Table("repository_cascade_row")]
    private sealed class RepositoryCascadeRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("deleted")]
        public long? Deleted { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    private sealed class TestCascadeRuleProvider(ExtensionCascadeRule rule) : IExtensionCascadeRuleProvider
    {
        public IReadOnlyList<ExtensionCascadeRule> Rules { get; } = [rule];
    }

    public enum RepositoryCascadeDeleteState
    {
        Live,
        Tombstoned,
        Missing
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
}
