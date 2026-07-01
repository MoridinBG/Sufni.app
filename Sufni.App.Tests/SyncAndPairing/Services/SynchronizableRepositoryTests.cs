using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.SyncAndPairing.Models;
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
}
