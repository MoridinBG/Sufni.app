using System;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Setups.Coordinators;

internal sealed class SetupPersistenceTransactionRunner(
    SqliteConnectionContext connectionContext,
    TimeProvider timeProvider)
    : ISetupPersistenceTransactionRunner
{
    public Task SaveSetupAsync(
        Setup setup,
        Guid? originalBoardId,
        Guid? newBoardId,
        CancellationToken cancellationToken = default)
    {
        return connectionContext.RunInTransactionAsync(connection =>
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup, now);
            ReassignBoardInTransaction(connection, originalBoardId, newBoardId, setup.Id, now);
        }, cancellationToken);
    }

    public Task DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        return connectionContext.RunInTransactionAsync(connection =>
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Setup>.DeleteInTransaction(connection, setupId, now);
            ReassignBoardInTransaction(connection, boardId, null, setupId, now);
        }, cancellationToken);
    }

    public Task ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        return connectionContext.RunInTransactionAsync(connection =>
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Bike>.PutInTransaction(connection, bike, now);
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup, now);
            ReassignBoardInTransaction(connection, originalBoardId: null, boardId, setup.Id, now);
        }, cancellationToken);
    }

    private static void ReassignBoardInTransaction(
        SQLiteConnection connection,
        Guid? originalBoardId,
        Guid? newBoardId,
        Guid setupId,
        long now)
    {
        if (originalBoardId == newBoardId) return;

        if (originalBoardId.HasValue)
        {
            SynchronizableRepository<Board>.PutInTransaction(connection, new Board(originalBoardId.Value, null), now);
        }

        if (newBoardId.HasValue)
        {
            SynchronizableRepository<Board>.PutInTransaction(connection, new Board(newBoardId.Value, setupId), now);
        }
    }
}
