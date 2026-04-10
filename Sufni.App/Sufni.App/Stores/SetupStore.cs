using System;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

internal sealed class SetupStore(IDatabaseService databaseService) : ISetupStoreWriter
{
    private readonly SourceCache<SetupSnapshot, Guid> source = new(s => s.Id);

    public IObservable<IChangeSet<SetupSnapshot, Guid>> Connect() => source.Connect();

    public SetupSnapshot? Get(Guid id)
    {
        var lookup = source.Lookup(id);
        return lookup.HasValue ? lookup.Value : null;
    }

    public SetupSnapshot? FindByBoardId(Guid boardId) =>
        source.Items.FirstOrDefault(s => s.BoardId == boardId);

    public async Task RefreshAsync()
    {
        var setups = await databaseService.GetAllAsync<Setup>();
        var boards = await databaseService.GetAllAsync<Board>();

        source.Edit(cache =>
        {
            cache.Clear();
            foreach (var setup in setups)
            {
                var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                cache.AddOrUpdate(SetupSnapshot.From(setup, board?.Id));
            }
        });
    }

    public void Upsert(SetupSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(Guid id) => source.RemoveKey(id);
}
