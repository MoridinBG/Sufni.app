using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemoryBikeTransactions(
    InMemorySynchronizableRepository<Bike> bikes,
    InMemorySynchronizableRepository<Setup> setups) : IBikePersistenceTransactionRunner
{
    public async Task<BikeDeletePersistenceResult> DeleteBikeAsync(Guid bikeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((await setups.GetAllAsync()).Any(setup => setup.BikeId == bikeId)) return new BikeDeletePersistenceResult.InUse();
        await bikes.DeleteAsync(bikeId);
        return new BikeDeletePersistenceResult.Deleted();
    }
}
