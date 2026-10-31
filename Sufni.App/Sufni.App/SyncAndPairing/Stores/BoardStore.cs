using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;

namespace Sufni.App.SyncAndPairing.Stores;

internal sealed class BoardStore(StorePublication publication)
    : SourceCacheStoreBase<BoardSnapshot, Guid>(board => board.Id, publication), IBoardStoreWriter
{
    public IReadOnlyList<BoardSnapshot> GetAll() => Items.ToArray();
    public Task PublishBoardsChangedAsync(IReadOnlyCollection<BoardSnapshot> snapshots) => PublishSnapshotsAsync(snapshots);
    public Task PublishBoardsRemovedAsync(IReadOnlyCollection<Guid> ids) => PublishRemovalsAsync(ids);
}
