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
}