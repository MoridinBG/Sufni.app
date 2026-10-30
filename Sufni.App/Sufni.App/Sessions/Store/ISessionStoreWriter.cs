using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Sessions.Store;

/// <summary>
/// Write surface for the session store. Convention: only the
/// composition root and coordinators should take a dependency on this
/// interface. View models, rows and queries take
/// <see cref="ISessionStore"/> instead.
/// </summary>
public interface ISessionStoreWriter : ISessionStore
{
    /// <summary>
    /// Replace the published contents with prepared snapshots.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<SessionSnapshot> snapshots);

    Task PublishSessionsChangedAsync(
        IReadOnlyCollection<SessionSnapshot> snapshots,
        CancellationToken cancellationToken = default);

    Task PublishSessionsRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);
}
