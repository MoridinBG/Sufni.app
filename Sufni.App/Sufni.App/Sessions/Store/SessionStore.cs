using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.Sessions.Store;

/// <summary>Shared session metadata; telemetry and source payloads remain in repositories.</summary>
internal sealed class SessionStore(StorePublication publication)
    : SourceCacheStoreBase<SessionSnapshot, Guid>(session => session.Id, publication), ISessionStoreWriter
{
    public IReadOnlyList<SessionSnapshot> GetAll() => Items.ToArray();

    public IObservable<SessionSnapshot> Watch(Guid id) =>
        WatchCore(id).Where(change => change.Reason is ChangeReason.Add or ChangeReason.Update)
            .Select(change => change.Current);

    public Task PublishSessionsChangedAsync(IReadOnlyCollection<SessionSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishSnapshotsAsync(snapshots);
    }

    public Task PublishSessionsRemovedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(sessionIds);
    }
}
