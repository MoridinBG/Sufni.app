using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Setups.Stores;

/// <summary>
/// Write surface for the setup store. Convention: only the composition
/// root and coordinators take a dependency on this interface.
/// Enforcement is by convention, not accessibility (same reasoning as
/// <see cref="IBikeStoreWriter"/>).
/// </summary>
public interface ISetupStoreWriter : ISetupStore
{
    /// <summary>
    /// Load setups (and their board associations) from the database
    /// and replace the current contents.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task PublishSetupsChangedAsync(
        IReadOnlyCollection<Guid> setupIds,
        CancellationToken cancellationToken = default);

    Task PublishSetupsRemovedAsync(
        IReadOnlyCollection<Guid> setupIds,
        CancellationToken cancellationToken = default);
}
