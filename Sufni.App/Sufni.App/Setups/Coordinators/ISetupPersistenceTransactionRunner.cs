using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.Setups.Coordinators;

public interface ISetupPersistenceTransactionRunner
{
    Task SaveSetupAsync(
        Setup setup,
        Guid? originalBoardId,
        Guid? newBoardId,
        CancellationToken cancellationToken = default);

    Task DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default);

    Task ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default);
}
