using System;
using System.Threading.Tasks;
using DynamicData;

namespace Sufni.App.Stores;

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
    /// Per-id observable used by <c>SessionDetailViewModel</c> to
    /// react to telemetry-arrival and recalculation events for a
    /// specific session. See "Telemetry-arrival semantics" in
    /// REFACTOR-PLAN.md for the contract.
    /// </summary>
    IObservable<SessionSnapshot> Watch(Guid id);

    /// <summary>
    /// Snapshot lookup by id. Returns null if the session is not in
    /// the store (e.g. never loaded, or deleted).
    /// </summary>
    SessionSnapshot? Get(Guid id);

    /// <summary>
    /// Load all sessions from the database and replace the current
    /// contents. Called once at startup; the store is otherwise
    /// mutated via <see cref="ISessionStoreWriter"/>.
    /// </summary>
    Task RefreshAsync();
}
