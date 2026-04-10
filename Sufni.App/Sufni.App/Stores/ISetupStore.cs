using System;
using System.Threading.Tasks;
using DynamicData;

namespace Sufni.App.Stores;

/// <summary>
/// Read-only view of the setup collection. Injected into list / editor
/// view models and queries. Mutations flow through
/// <see cref="ISetupStoreWriter"/> and are reserved for coordinators
/// and the composition root.
/// </summary>
public interface ISetupStore
{
    /// <summary>
    /// DynamicData change stream. List view models build filtered
    /// projections to <c>SetupRowViewModel</c> instances from this.
    /// </summary>
    IObservable<IChangeSet<SetupSnapshot, Guid>> Connect();

    /// <summary>
    /// Snapshot lookup by id. Null if the setup is not in the store.
    /// </summary>
    SetupSnapshot? Get(Guid id);

    /// <summary>
    /// Look up a snapshot by its associated DAQ board id.
    /// Used by import to prefill the "setup for this board" selection.
    /// </summary>
    SetupSnapshot? FindByBoardId(Guid boardId);

    /// <summary>
    /// Load setups (and their board associations) from the database
    /// and replace the current contents.
    /// </summary>
    Task RefreshAsync();
}
