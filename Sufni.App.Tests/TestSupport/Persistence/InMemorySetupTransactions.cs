using System.Collections.Immutable;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemorySetupTransactions(
    InMemorySynchronizableRepository<Setup> setups,
    InMemorySynchronizableRepository<Bike> bikes,
    InMemorySynchronizableRepository<Board> boards,
    InMemorySynchronizableRepository<Session> sessions) : ISetupPersistenceTransactionRunner
{
    public Exception? Failure { get; set; }

    public async Task<SetupWriteResult> SaveSetupAsync(Setup setup, Guid? originalBoardId, Guid? newBoardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure) throw failure;
        if (await bikes.GetAsync(setup.BikeId) is null) return new SetupWriteResult.BikeMissing();
        if (await BoardIsInUseAsync(newBoardId, setup.Id)) return new SetupWriteResult.BoardInUse(newBoardId!.Value);
        await setups.PutAsync(setup);
        return new SetupWriteResult.Applied(await ReassignAsync(originalBoardId, newBoardId, setup.Id));
    }

    public async Task<SetupDeletePersistenceResult> DeleteSetupAsync(Guid setupId, Guid? boardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure) throw failure;
        if ((await sessions.GetAllAsync()).Any(session => session.Setup == setupId)) return new SetupDeletePersistenceResult.InUse();
        await setups.DeleteAsync(setupId);
        return new SetupDeletePersistenceResult.Deleted(await ReassignAsync(boardId, null, setupId));
    }

    public async Task<SetupWriteResult> ImportSetupAsync(Bike bike, Setup setup, Guid? boardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure) throw failure;
        if (await BoardIsInUseAsync(boardId, setup.Id)) return new SetupWriteResult.BoardInUse(boardId!.Value);
        await bikes.PutAsync(bike);
        await setups.PutAsync(setup);
        return new SetupWriteResult.Applied(await ReassignAsync(null, boardId, setup.Id));
    }

    private async Task<bool> BoardIsInUseAsync(Guid? boardId, Guid setupId) =>
        boardId is { } id && await boards.GetAsync(id) is { SetupId: { } owner } && owner != setupId;

    private async Task<ImmutableArray<Guid>> ReassignAsync(Guid? previous, Guid? next, Guid setupId)
    {
        var changed = ImmutableArray.CreateBuilder<Guid>();
        if (previous is { } oldId && previous != next && await boards.GetAsync(oldId) is { SetupId: { } owner } && owner == setupId)
        {
            await boards.PutAsync(new Board(oldId, null));
            changed.Add(oldId);
        }
        if (next is { } newId && (await boards.GetAsync(newId))?.SetupId != setupId)
        {
            await boards.PutAsync(new Board(newId, setupId));
            changed.Add(newId);
        }
        return changed.ToImmutable();
    }
}
