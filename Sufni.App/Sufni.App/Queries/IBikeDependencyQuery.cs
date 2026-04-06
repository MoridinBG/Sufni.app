using System;
using System.Threading.Tasks;

namespace Sufni.App.Queries;

/// <summary>
/// Answers business questions about bike references held by other
/// entities. Backed by <see cref="Services.IDatabaseService"/> so the
/// answer does not depend on which screens the user has visited.
/// </summary>
public interface IBikeDependencyQuery
{
    /// <summary>
    /// True if any setup currently references the bike. Used to block
    /// delete.
    /// </summary>
    Task<bool> IsBikeInUseAsync(Guid bikeId);
}
