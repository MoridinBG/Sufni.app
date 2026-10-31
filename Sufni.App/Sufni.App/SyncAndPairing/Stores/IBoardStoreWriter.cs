using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.SyncAndPairing.Stores;

public interface IBoardStoreWriter : IBoardStore
{
    Task ReplaceAllAsync(IReadOnlyList<BoardSnapshot> snapshots);
    Task PublishBoardsChangedAsync(IReadOnlyCollection<BoardSnapshot> snapshots);
    Task PublishBoardsRemovedAsync(IReadOnlyCollection<Guid> ids);
}
