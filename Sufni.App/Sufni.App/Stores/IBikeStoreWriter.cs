using System;

namespace Sufni.App.Stores;

/// <summary>
/// Write surface for the bike store. Convention: only the composition
/// root and coordinators should take a dependency on this interface.
/// View models, rows and queries take <see cref="IBikeStore"/> instead.
///
/// The interface is public because <c>MainPagesViewModel</c> (a public
/// type) is injected with it during the transitional phase while sync
/// handlers are still in the shell. Once Slice 5 lands a sync
/// coordinator, the shell no longer needs the writer and this interface
/// can be narrowed again — but the enforcement stays as convention, not
/// accessibility, since coordinators will still need to reference it.
/// </summary>
public interface IBikeStoreWriter : IBikeStore
{
    /// <summary>
    /// Insert or replace the snapshot for a bike. Typically called after
    /// a coordinator has persisted changes or after a sync arrival.
    /// </summary>
    void Upsert(BikeSnapshot snapshot);

    /// <summary>
    /// Remove a bike from the store by id. No-op if it is not present.
    /// </summary>
    void Remove(Guid id);
}
