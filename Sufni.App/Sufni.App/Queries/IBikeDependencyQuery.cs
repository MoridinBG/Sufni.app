using System;
using System.Threading.Tasks;

namespace Sufni.App.Queries;

/// <summary>
/// Answers business questions about bike references held by other
/// entities.
/// </summary>
public interface IBikeDependencyQuery
{
    /// <summary>
    /// True if any setup currently references the bike. Backed by
    /// <see cref="Services.IDatabaseService"/> for the authoritative
    /// answer used inside coordinator delete checks.
    /// </summary>
    Task<bool> IsBikeInUseAsync(Guid bikeId);

    /// <summary>
    /// Synchronous variant backed by the in-memory
    /// <see cref="Stores.ISetupStore"/>. Used by view models that need
    /// to drive <c>CanExecute</c> on a delete command. The DB-backed
    /// <see cref="IsBikeInUseAsync"/> remains the authoritative check
    /// inside the bike coordinator's delete path.
    /// </summary>
    bool IsBikeInUse(Guid bikeId);
}
