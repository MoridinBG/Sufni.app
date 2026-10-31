using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.Setups.Coordinators;

/// <summary>Commits setup writes and returns the affected board identities for publication.</summary>
public interface ISetupPersistenceTransactionRunner
{
    Task<IReadOnlyList<Guid>> SaveSetupAsync(
        Setup setup,
        Guid? originalBoardId,
        Guid? newBoardId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default);
}
