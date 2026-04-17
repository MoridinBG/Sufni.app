using System.Collections.Generic;

namespace Sufni.App.Stores;

/// <summary>
/// Write surface for the runtime live DAQ store. Reserved for the
/// composition root and feature coordinators.
/// </summary>
public interface ILiveDaqStoreWriter : ILiveDaqStore
{
    // Adds a new row or replaces the current row for the same identity.
    void Upsert(LiveDaqSnapshot snapshot);

    // Removes one runtime row if it exists.
    void Remove(string identityKey);

    // Clears the runtime catalog before a fresh reconciliation pass.
    void Clear();

    // Atomically replaces the entire catalog so observers see one coherent
    // changeset instead of a transient empty state.
    void ReplaceAll(IEnumerable<LiveDaqSnapshot> snapshots);
}