using System.Collections.Generic;
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
    TimeProvider timeProvider,
    ILocalDeviceIdentity localDeviceIdentity)
    : ISetupPersistenceTransactionRunner
{
    public async Task<IReadOnlyList<Guid>> SaveSetupAsync(
        Setup setup,
        Guid? originalBoardId,
        Guid? newBoardId,
        CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync(connection =>
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup, now, writer);
            return ReassignBoardInTransaction(connection, originalBoardId, newBoardId, setup.Id, now, writer);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync(connection =>
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Setup>.DeleteInTransaction(connection, setupId, now, writer);
            return ReassignBoardInTransaction(connection, boardId, null, setupId, now, writer);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync(connection =>
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Bike>.PutInTransaction(connection, bike, now, writer);
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup, now, writer);
            return ReassignBoardInTransaction(connection, originalBoardId: null, boardId, setup.Id, now, writer);
        }, cancellationToken);
    }

    private static IReadOnlyList<Guid> ReassignBoardInTransaction(
        SQLiteConnection connection,
        Guid? originalBoardId,
        Guid? newBoardId,
        Guid setupId,
        long now, string writer)
    {
        if (originalBoardId == newBoardId) return [];
        var changed = new List<Guid>();

        if (originalBoardId.HasValue)
        {
            SynchronizableRepository<Board>.PutInTransaction(connection, new Board(originalBoardId.Value, null), now, writer);
            changed.Add(originalBoardId.Value);
        }

        if (newBoardId.HasValue)
        {
            SynchronizableRepository<Board>.PutInTransaction(connection, new Board(newBoardId.Value, setupId), now, writer);
            changed.Add(newBoardId.Value);
        }
        return changed;
    }
}
