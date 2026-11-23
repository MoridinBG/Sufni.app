using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.Telemetry;

namespace Sufni.App.Bikes.Coordinators;

/// <summary>Commits bike writes with their conflict and dependency checks.</summary>
public interface IBikePersistenceTransactionRunner
{
    Task<BikeSaveResult> SaveAsync(BikeDraft draft, BikeDraft? baseline);
    Task<BikeDampingSpeedCutoffUpdateResult> UpdateCutoffAsync(Guid id, double expected,
        SuspensionType side, DampingSpeedCircuit circuit, double value);
    Task<BikeDeletePersistenceResult> DeleteBikeAsync(Guid bikeId, CancellationToken cancellationToken = default);
}

public closed record BikeDeletePersistenceResult
{
    private BikeDeletePersistenceResult() { }
    public sealed record Deleted : BikeDeletePersistenceResult;
    public sealed record InUse : BikeDeletePersistenceResult;
}
