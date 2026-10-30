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
    /// Replace the published contents with prepared snapshots.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<SetupSnapshot> snapshots);

    Task PublishSetupsChangedAsync(
        IReadOnlyCollection<SetupSnapshot> snapshots,
        CancellationToken cancellationToken = default);

    Task PublishSetupsRemovedAsync(
        IReadOnlyCollection<Guid> setupIds,
        CancellationToken cancellationToken = default);
}
