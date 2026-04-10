using System;

namespace Sufni.App.Stores;

/// <summary>
/// Write surface for the setup store. Convention: only the composition
/// root and coordinators take a dependency on this interface.
/// Enforcement is by convention, not accessibility (same reasoning as
/// <see cref="IBikeStoreWriter"/>).
/// </summary>
public interface ISetupStoreWriter : ISetupStore
{
    /// <summary>
    /// Insert or replace the snapshot for a setup.
    /// </summary>
    void Upsert(SetupSnapshot snapshot);

    /// <summary>
    /// Remove a setup from the store by id. No-op if not present.
    /// </summary>
    void Remove(Guid id);
}
