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
    /// Load all sessions from the database and replace the current
    /// contents.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task PublishSessionsChangedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    Task PublishSessionsRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or replace the snapshot for a session. Typically called
    /// by <c>SessionCoordinator</c> after a save, after sync arrival,
    /// or after the local mobile telemetry-fetch path patches the
    /// database.
    /// </summary>
    void Upsert(SessionSnapshot snapshot);

    /// <summary>
    /// Remove a session from the store by id. No-op if it is not
    /// present.
    /// </summary>
    void Remove(Guid id);
}
