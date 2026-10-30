using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemorySetupTransactions(
    InMemorySynchronizableRepository<Setup> setups,
    InMemorySynchronizableRepository<Bike> bikes,
    InMemorySynchronizableRepository<Board> boards) : ISetupPersistenceTransactionRunner
{
    public Exception? Failure { get; set; }

    public async Task SaveSetupAsync(Setup setup, Guid? originalBoardId, Guid? newBoardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure) throw failure;
        await setups.PutAsync(setup);
        await ReassignAsync(originalBoardId, newBoardId, setup.Id);
    }

    public async Task DeleteSetupAsync(Guid setupId, Guid? boardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure) throw failure;
        await setups.DeleteAsync(setupId);
        await ReassignAsync(boardId, null, setupId);
    }

    public async Task ImportSetupAsync(Bike bike, Setup setup, Guid? boardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure) throw failure;
        await bikes.PutAsync(bike);
        await setups.PutAsync(setup);
        await ReassignAsync(null, boardId, setup.Id);
    }

    private async Task ReassignAsync(Guid? previous, Guid? next, Guid setupId)
    {
        if (previous == next) return;
        if (previous is { } oldBoard) await boards.PutAsync(new Board(oldBoard, null));
        if (next is { } newBoard) await boards.PutAsync(new Board(newBoard, setupId));
    }
}
