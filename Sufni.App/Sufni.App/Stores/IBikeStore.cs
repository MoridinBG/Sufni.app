using System;
using System.Threading.Tasks;
using DynamicData;

namespace Sufni.App.Stores;

/// <summary>
/// Read-only view of the bike collection. Injected into row/list view
/// models and queries. The write surface lives on
/// <see cref="IBikeStoreWriter"/> and is reserved for coordinators.
/// </summary>
public interface IBikeStore
{
    /// <summary>
    /// DynamicData change stream. List view models use this to build
    /// filtered projections to row view models.
    /// </summary>
    IObservable<IChangeSet<BikeSnapshot, Guid>> Connect();

    /// <summary>
    /// Snapshot lookup by id. Returns null if the bike is not in the
    /// store (e.g. never loaded, or deleted).
    /// </summary>
    BikeSnapshot? Get(Guid id);

    /// <summary>
    /// Load all bikes from the database and replace the current contents.
    /// Called once at startup; the store is otherwise mutated via
    /// <see cref="IBikeStoreWriter"/>.
    /// </summary>
    Task RefreshAsync();
}
