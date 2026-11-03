using System.Collections.Immutable;
using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.Setups.Coordinators;

/// <summary>Commits setup writes and returns the affected board identities for publication.</summary>
public interface ISetupPersistenceTransactionRunner
{
    Task<SetupWriteResult> SaveSetupAsync(
        Setup setup,
        Guid? originalBoardId,
        Guid? newBoardId,
        CancellationToken cancellationToken = default);

    Task<SetupDeletePersistenceResult> DeleteSetupAsync(
        Guid setupId,
        Guid? boardId,
        CancellationToken cancellationToken = default);

    Task<SetupWriteResult> ImportSetupAsync(
        Bike bike,
        Setup setup,
        Guid? boardId,
        CancellationToken cancellationToken = default);
}

public abstract record SetupWriteResult
{
    private SetupWriteResult() { }
    public sealed record Applied(ImmutableArray<Guid> ChangedBoardIds) : SetupWriteResult;
    public sealed record BoardInUse(Guid BoardId) : SetupWriteResult;
    /// <summary>Save only: the setup's bike is missing or deleted. Import writes its own bike.</summary>
    public sealed record BikeMissing : SetupWriteResult;
}

public abstract record SetupDeletePersistenceResult
{
    private SetupDeletePersistenceResult() { }
    public sealed record Deleted(ImmutableArray<Guid> ChangedBoardIds) : SetupDeletePersistenceResult;
    public sealed record InUse : SetupDeletePersistenceResult;
}
