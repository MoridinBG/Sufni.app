using SQLite;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizableRepositoryTests
{
    [Fact]
    public async Task SameSecondEdits_AdvanceVersionAndSequences_WithStableWriter()
    {
        using var temp = new TempDatabase();
        var database = new TestPersistenceHarness(temp.DatabasePath, writerId: "writer-a");
        var board = new Board(Guid.NewGuid(), null);
        await database.PutAsync(board);
        var first = (await database.GetAsync<Board>(board.Id))!;
        var assignedSetup = Guid.NewGuid();

        await database.PutAsync(new Board(board.Id, assignedSetup));

        var second = (await database.GetAsync<Board>(board.Id))!;
        Assert.Equal(first.Updated, second.Updated);
        Assert.Equal(first.Version + 1, second.Version);
        Assert.Equal("writer-a", second.UpdatedBy);
        Assert.True(second.LocalSequence > first.LocalSequence);
        Assert.True(second.SyncSequence > first.SyncSequence);
    }

    [Fact]
    public async Task DeleteLedger_SurvivesPayloadPurge_AndExplicitRestoreAdvancesPastIt()
    {
        using var temp = new TempDatabase();
        var clock = PersistenceTestData.CreateTimeProvider();
        var database = new TestPersistenceHarness(temp.DatabasePath, clock, "writer-a");
        var board = new Board(Guid.NewGuid(), null);
        await database.PutAsync(board);
        await database.DeleteAsync(board);
        var connection = await database.GetInitializedConnectionAsync();
        var deletedVersion = await connection.ExecuteScalarAsync<long>("SELECT version FROM sync_deletion WHERE entity_table='board' AND id=?", board.Id);
        clock.Advance(TimeSpan.FromDays(2));
        var restarted = new TestPersistenceHarness(temp.DatabasePath, clock, "writer-a");
        Assert.Empty(await restarted.GetAllAsync<Board>());
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM board WHERE id=?", board.Id));

        await restarted.PutAsync(new Board(board.Id, Guid.NewGuid()));

        var restored = (await restarted.GetAsync<Board>(board.Id))!;
        Assert.Equal(deletedVersion + 1, restored.Version);
        Assert.Equal(deletedVersion, await connection.ExecuteScalarAsync<long>("SELECT version FROM sync_deletion WHERE entity_table='board' AND id=?", board.Id));
    }

    [Fact]
    public async Task AuthoredWriteAndDeletion_UseInjectedTimeInChangeWindow()
    {
        using var tempDatabase = new TempDatabase("board-clock.db");
        var time = PersistenceTestData.CreateTimeProvider();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath, time);
        var board = new Board(Guid.NewGuid(), null);
        var createdAt = time.GetUtcNow().ToUnixTimeSeconds();
        await database.PutAsync(board);
        time.Advance(TimeSpan.FromSeconds(10));

        await database.DeleteAsync(board);

        var deleted = Assert.Single(await database.GetChangedAsync<Board>(createdAt, createdAt + 10));
        Assert.Equal(createdAt + 10, deleted.Updated);
        Assert.Equal(createdAt + 10, deleted.Deleted);
    }

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

                Deleted = 100
            });
        }

        await database.DeleteAsync(new Board(boardId, null));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(100, board.Deleted);

    }
}
