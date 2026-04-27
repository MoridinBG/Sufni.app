using System;

namespace Sufni.App.Stores;

/// Write surface for the bike store.
public interface IBikeStoreWriter : IBikeStore
{
    /// Insert or replace the snapshot for a bike.
    void Upsert(BikeSnapshot snapshot);

    /// Remove a bike from the store by id. No-op if it is not present.
    void Remove(Guid id);
}
