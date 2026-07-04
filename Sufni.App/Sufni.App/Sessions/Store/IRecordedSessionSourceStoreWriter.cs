using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Sessions.Store;

/// <summary>
/// Write side of the recorded-session source cache.
/// It owns direct metadata updates after the database has already changed.
/// </summary>
public interface IRecordedSessionSourceStoreWriter : IRecordedSessionSourceStore
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task PublishSourcesChangedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    Task PublishSourcesRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    void Upsert(RecordedSessionSourceSnapshot snapshot);
    void Remove(Guid sessionId);
}
