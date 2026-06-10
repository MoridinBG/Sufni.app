using System;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

internal sealed class SetupStore(
    ISynchronizableRepository<Setup> setupRepository,
    ISynchronizableRepository<Board> boardRepository)
    : SourceCacheStoreBase<SetupSnapshot, Guid>(s => s.Id), ISetupStoreWriter
{
    public SetupSnapshot? FindByBoardId(Guid boardId) =>
        Items.FirstOrDefault(s => s.BoardId == boardId);

    public async Task RefreshAsync()
    {
        var setups = await setupRepository.GetAllAsync();
        var boards = await boardRepository.GetAllAsync();

        ReplaceWith(setups.Select(setup =>
        {
            var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
            return SetupSnapshot.From(setup, board?.Id);
        }));
    }
}
