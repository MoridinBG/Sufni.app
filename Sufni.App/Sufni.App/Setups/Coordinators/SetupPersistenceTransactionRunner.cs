using System;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

using Sufni.App.Bikes.Models;
using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Setups.Coordinators;

internal sealed class SetupPersistenceTransactionRunner(
    SqliteConnectionContext connectionContext,
    IExtensionCascadeService? extensionCascadeService = null)
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
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup);
            ReassignBoardInTransaction(connection, originalBoardId, newBoardId, setup.Id);
        }, cancellationToken);
    }

    public async Task DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        var rulesApplied = false;
        await connectionContext.RunInTransactionAsync(connection =>
        {
            rulesApplied = SynchronizableRepository<Setup>.DeleteInTransaction(
                connection,
                setupId,
                extensionCascadeService);
            ReassignBoardInTransaction(connection, boardId, null, setupId);
        }, cancellationToken);

        if (rulesApplied && extensionCascadeService is not null)
        {
            await extensionCascadeService.RefreshExtensionStateAsync(cancellationToken);
        }
    }

    public Task ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        return connectionContext.RunInTransactionAsync(connection =>
        {
            SynchronizableRepository<Bike>.PutInTransaction(connection, bike);
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup);
            ReassignBoardInTransaction(connection, originalBoardId: null, boardId, setup.Id);
        }, cancellationToken);
    }

    private static void ReassignBoardInTransaction(
        SQLiteConnection connection,
        Guid? originalBoardId,
        Guid? newBoardId,
        Guid setupId)
    {
        if (originalBoardId == newBoardId) return;

        if (originalBoardId.HasValue)
        {
            SynchronizableRepository<Board>.PutInTransaction(connection, new Board(originalBoardId.Value, null));
        }

        if (newBoardId.HasValue)
        {
            SynchronizableRepository<Board>.PutInTransaction(connection, new Board(newBoardId.Value, setupId));
        }
    }
}
