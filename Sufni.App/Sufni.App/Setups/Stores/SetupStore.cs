using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.Setups.Stores;

internal sealed class SetupStore(StorePublication publication)
    : SourceCacheStoreBase<SetupSnapshot, Guid>(setup => setup.Id, publication), ISetupStoreWriter
{
    public SetupSnapshot? FindByBoardId(Guid boardId) =>
        Items.FirstOrDefault(setup => setup.BoardId == boardId);

    public Task PublishSetupsChangedAsync(IReadOnlyCollection<SetupSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishSnapshotsAsync(snapshots);
    }

    public Task PublishSetupsRemovedAsync(IReadOnlyCollection<Guid> setupIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(setupIds);
    }
}
