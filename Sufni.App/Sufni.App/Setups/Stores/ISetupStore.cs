using System.Collections.Generic;
using System;
using DynamicData;

namespace Sufni.App.Setups.Stores;

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

    IReadOnlyList<SetupSnapshot> GetAll();
}
