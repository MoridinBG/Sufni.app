using System.Collections.Generic;

namespace Sufni.App.LiveDaq.Stores;

/// <summary>
/// Write surface for the runtime live DAQ store. Reserved for the
/// composition root and feature coordinators.
/// </summary>
public interface ILiveDaqStoreWriter : ILiveDaqStore
{
    // Atomically replaces the entire catalog so observers see one coherent
    // changeset instead of a transient empty state.
    void ReplaceAll(IEnumerable<LiveDaqSnapshot> snapshots);
}