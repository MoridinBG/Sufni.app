using System;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

internal sealed class SetupStore(IDatabaseService databaseService)
    : SourceCacheStoreBase<SetupSnapshot, Guid>(s => s.Id), ISetupStoreWriter
{
    public SetupSnapshot? FindByBoardId(Guid boardId) =>
        Items.FirstOrDefault(s => s.BoardId == boardId);

    public async Task RefreshAsync()
    {
        var setups = await databaseService.GetAllAsync<Setup>();
        var boards = await databaseService.GetAllAsync<Board>();

        ReplaceWith(setups.Select(setup =>
        {
            var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
            return SetupSnapshot.From(setup, board?.Id);
        }));
    }
}
