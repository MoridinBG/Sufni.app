using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.Telemetry;
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
    public async Task<BikeSaveResult> SaveAsync(BikeDraft draft, BikeDraft? baseline)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync<BikeSaveResult>(connection =>
        {
            var current = connection.Find<Bike>(draft.Id);
            if (current?.Deleted is not null) current = null;
            if (current is null && baseline is not null) return new BikeSaveResult.Missing();
            if (current is not null)
            {
                var currentDraft = BikeDraft.From(current);
                if (currentDraft != baseline) return new BikeSaveResult.Conflict(currentDraft);
            }

            var row = draft.ToRow(current?.DampingSpeedCutoffs);
            SynchronizableRepository<Bike>.PutInTransaction(connection, row, timeProvider.GetUtcNow().ToUnixTimeSeconds(), writer);
            return new BikeSaveResult.Saved(BikeSnapshot.From(row));
        });
    }

    public async Task<BikeDampingSpeedCutoffUpdateResult> UpdateCutoffAsync(Guid id, double expected,
        SuspensionType side, DampingSpeedCircuit circuit, double value)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync<BikeDampingSpeedCutoffUpdateResult>(connection =>
        {
            if (connection.Find<Bike>(id) is not { Deleted: null } row)
                return new BikeDampingSpeedCutoffUpdateResult.Missing();
            if (row.DampingSpeedCutoffs.Get(side, circuit) != expected)
                return new BikeDampingSpeedCutoffUpdateResult.Conflict(BikeSnapshot.From(row));

            row.DampingSpeedCutoffs = row.DampingSpeedCutoffs.With(side, circuit, DampingCutoffEditing.RoundDragValue(value));
            SynchronizableRepository<Bike>.PutInTransaction(connection, row, timeProvider.GetUtcNow().ToUnixTimeSeconds(), writer);
            return new BikeDampingSpeedCutoffUpdateResult.Saved(BikeSnapshot.From(row));
        });
    }

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
