using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.Sessions.Store;

/// <summary>Shared metadata for recorded sources; payloads remain in the repository.</summary>
internal sealed class RecordedSessionSourceStore(StorePublication publication)
    : SourceCacheStoreBase<RecordedSessionSourceSnapshot, Guid>(source => source.SessionId, publication), IRecordedSessionSourceStoreWriter
{
    public Task PublishSourcesChangedAsync(IReadOnlyCollection<RecordedSessionSourceSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishSnapshotsAsync(snapshots);
    }

    public Task PublishSourcesRemovedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(sessionIds);
    }
}
