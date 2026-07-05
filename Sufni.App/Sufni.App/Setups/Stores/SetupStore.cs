using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.Stores;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Setups.Stores;

internal sealed class SetupStore(
    ISynchronizableRepository<Setup> setupRepository,
    ISynchronizableRepository<Board> boardRepository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<SetupSnapshot, Guid>(s => s.Id, uiThreadDispatcher), ISetupStoreWriter
{
    public SetupSnapshot? FindByBoardId(Guid boardId) =>
        Items.FirstOrDefault(s => s.BoardId == boardId);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var setups = await setupRepository.GetAllAsync();
        var boards = await boardRepository.GetAllAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await ReplaceWithAsync(setups.Select(setup =>
        {
            var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
            return SetupSnapshot.From(setup, board?.Id);
        }));
    }

    public async Task PublishSetupsChangedAsync(
        IReadOnlyCollection<Guid> setupIds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<SetupSnapshot>();
        var removedIds = new List<Guid>();
        var boards = await boardRepository.GetAllAsync();

        foreach (var setupId in setupIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var setup = await setupRepository.GetAsync(setupId);
            if (setup is null)
            {
                removedIds.Add(setupId);
            }
            else
            {
                var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                snapshots.Add(SetupSnapshot.From(setup, board?.Id));
            }
        }

        if (snapshots.Count > 0)
        {
            await PublishSnapshotsAsync(snapshots);
        }

        if (removedIds.Count > 0)
        {
            await PublishRemovalsAsync(removedIds);
        }
    }

    public Task PublishSetupsRemovedAsync(
        IReadOnlyCollection<Guid> setupIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(setupIds.Distinct());
    }
}
