using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Bikes.Coordinators;

internal sealed class BikePersistenceTransactionRunner(
    SqliteConnectionContext connectionContext,
    TimeProvider timeProvider,
    ILocalDeviceIdentity localDeviceIdentity)
    : IBikePersistenceTransactionRunner
{
    public async Task<BikeDeletePersistenceResult> DeleteBikeAsync(Guid bikeId, CancellationToken cancellationToken = default)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync<BikeDeletePersistenceResult>(connection =>
        {
            if (connection.Table<Setup>().Where(setup => setup.Deleted == null && setup.BikeId == bikeId).Count() > 0)
                return new BikeDeletePersistenceResult.InUse();
            SynchronizableRepository<Bike>.DeleteInTransaction(connection, bikeId, timeProvider.GetUtcNow().ToUnixTimeSeconds(), writer);
            return new BikeDeletePersistenceResult.Deleted();
        }, cancellationToken);
    }
}
