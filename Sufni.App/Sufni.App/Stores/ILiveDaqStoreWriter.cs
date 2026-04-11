namespace Sufni.App.Stores;

/// <summary>
/// Write surface for the runtime live DAQ store. Reserved for the
/// composition root and feature coordinators.
/// </summary>
public interface ILiveDaqStoreWriter : ILiveDaqStore
{
    void Upsert(LiveDaqSnapshot snapshot);

    void Remove(string identityKey);

    void Clear();
}