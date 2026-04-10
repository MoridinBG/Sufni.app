using System;

namespace Sufni.App.Stores;

/// <summary>
/// Write surface for the bike store. By convention only the composition
/// root and coordinators take a dependency on this; view models, rows
/// and queries take <see cref="IBikeStore"/> instead.
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
