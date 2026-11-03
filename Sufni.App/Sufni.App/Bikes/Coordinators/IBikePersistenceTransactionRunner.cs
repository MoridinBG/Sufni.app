using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Bikes.Coordinators;

/// <summary>Deletes a bike only when no active setup references it, in one transaction.</summary>
public interface IBikePersistenceTransactionRunner
{
    Task<BikeDeletePersistenceResult> DeleteBikeAsync(Guid bikeId, CancellationToken cancellationToken = default);
}

public abstract record BikeDeletePersistenceResult
{
    private BikeDeletePersistenceResult() { }
    public sealed record Deleted : BikeDeletePersistenceResult;
    public sealed record InUse : BikeDeletePersistenceResult;
}
