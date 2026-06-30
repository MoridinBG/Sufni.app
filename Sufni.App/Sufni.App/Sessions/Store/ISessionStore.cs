using System;
using DynamicData;

namespace Sufni.App.Sessions.Store;

/// <summary>
/// Read-only view of the session collection. Injected into row/list
/// view models, the session detail editor, and queries. The write
/// surface lives on <see cref="ISessionStoreWriter"/> and is reserved
/// for coordinators.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// DynamicData change stream. The session list view model uses
    /// this to build a filtered, sorted projection to row VMs.
    /// </summary>
    IObservable<IChangeSet<SessionSnapshot, Guid>> Connect();

    /// <summary>
    /// Per-id observable used by <see cref="SessionSnapshot"/>
    /// consumers to react to telemetry-arrival and recalculation
    /// events for a specific session.
    /// </summary>
    IObservable<SessionSnapshot> Watch(Guid id);

    /// <summary>
    /// Snapshot lookup by id. Returns null if the session is not in
    /// the store (e.g. never loaded, or deleted).
    /// </summary>
    SessionSnapshot? Get(Guid id);

}
