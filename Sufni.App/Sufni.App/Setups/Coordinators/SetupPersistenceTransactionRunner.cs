using System.Collections.Immutable;
using Sufni.App.Sessions.Models;
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
    public async Task<SetupWriteResult> SaveSetupAsync(
        Setup setup,
        Guid? originalBoardId,
        Guid? newBoardId,
        CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync<SetupWriteResult>(connection =>
        {
            if (connection.Find<Bike>(setup.BikeId) is not { Deleted: null }) return new SetupWriteResult.BikeMissing();
            if (BoardIsInUse(connection, newBoardId, setup.Id)) return new SetupWriteResult.BoardInUse(newBoardId!.Value);
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup, now, writer);
            return new SetupWriteResult.Applied(ReassignBoardInTransaction(connection, originalBoardId, newBoardId, setup.Id, now, writer));
        }, cancellationToken);
    }

    public async Task<SetupDeletePersistenceResult> DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync<SetupDeletePersistenceResult>(connection =>
        {
            if (connection.Table<Session>().Where(session => session.Deleted == null && session.Setup == setupId).Count() > 0)
                return new SetupDeletePersistenceResult.InUse();
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Setup>.DeleteInTransaction(connection, setupId, now, writer);
            return new SetupDeletePersistenceResult.Deleted(ReassignBoardInTransaction(connection, boardId, null, setupId, now, writer));
        }, cancellationToken);
    }

    public async Task<SetupWriteResult> ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync<SetupWriteResult>(connection =>
        {
            if (BoardIsInUse(connection, boardId, setup.Id)) return new SetupWriteResult.BoardInUse(boardId!.Value);
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            SynchronizableRepository<Bike>.PutInTransaction(connection, bike, now, writer);
            SynchronizableRepository<Setup>.PutInTransaction(connection, setup, now, writer);
            return new SetupWriteResult.Applied(ReassignBoardInTransaction(connection, originalBoardId: null, boardId, setup.Id, now, writer));
        }, cancellationToken);
    }

    private static bool BoardIsInUse(SQLiteConnection connection, Guid? boardId, Guid setupId) =>
        boardId is { } id && connection.Find<Board>(id) is { Deleted: null, SetupId: { } owner } && owner != setupId;

    private static ImmutableArray<Guid> ReassignBoardInTransaction(
        SQLiteConnection connection, Guid? originalBoardId, Guid? newBoardId, Guid setupId, long now, string writer)
    {
        var changed = ImmutableArray.CreateBuilder<Guid>();
        if (originalBoardId is { } oldId && originalBoardId != newBoardId &&
            connection.Find<Board>(oldId) is { Deleted: null, SetupId: { } owner } previous && owner == setupId)
        {
            previous.SetupId = null;
            SynchronizableRepository<Board>.PutInTransaction(connection, previous, now, writer);
            changed.Add(oldId);
        }
        if (newBoardId is { } newId)
        {
            var current = connection.Find<Board>(newId);
            if (current is not { Deleted: null } || current.SetupId != setupId)
            {
                SynchronizableRepository<Board>.PutInTransaction(connection, new Board(newId, setupId), now, writer);
                changed.Add(newId);
            }
        }
        return changed.ToImmutable();
    }
}
