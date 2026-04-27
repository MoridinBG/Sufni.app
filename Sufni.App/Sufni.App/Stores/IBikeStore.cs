using System;
using System.Threading.Tasks;
using DynamicData;

namespace Sufni.App.Stores;

/// Read-only view of the bike collection.
public interface IBikeStore
{
    /// DynamicData change stream.
    IObservable<IChangeSet<BikeSnapshot, Guid>> Connect();

    /// Snapshot lookup by id. Returns null if the bike is not in the
    /// store (e.g. never loaded, or deleted).
    BikeSnapshot? Get(Guid id);

    /// Load all bikes from the database and replace the current contents.
    Task RefreshAsync();
}
