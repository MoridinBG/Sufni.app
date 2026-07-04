using System;
using System.Linq;
using System.Threading.Tasks;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Shared.Base;
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

    public async Task RefreshAsync()
    {
        var setups = await setupRepository.GetAllAsync();
        var boards = await boardRepository.GetAllAsync();

        await ReplaceWithAsync(setups.Select(setup =>
        {
            var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
            return SetupSnapshot.From(setup, board?.Id);
        }));
    }

    public void Upsert(SetupSnapshot snapshot) =>
        PublishSnapshotAsync(snapshot).GetAwaiter().GetResult();

    public void Remove(Guid id) =>
        PublishRemoveAsync(id).GetAwaiter().GetResult();
}
