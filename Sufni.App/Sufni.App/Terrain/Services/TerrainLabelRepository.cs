using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Services;

public interface ITerrainLabelRepository
{
    /// <summary>
    /// Returns the live terrain labels.
    /// </summary>
    Task<List<TerrainLabel>> GetLiveAsync();

    Task InsertAsync(TerrainLabel label);

    /// <summary>
    /// Returns the updated row, or null when the label is missing or deleted.
    /// </summary>
    Task<TerrainLabel?> SetClassAsync(Guid labelId, TerrainLabelClass labelClass);

    /// <summary>
    /// Returns the deleted row, or null when the label is missing or already deleted.
    /// </summary>
    Task<TerrainLabel?> DeleteAsync(Guid labelId);
}

internal sealed class TerrainLabelRepository(SqliteConnectionContext connectionContext, TimeProvider timeProvider,
    ILocalDeviceIdentity localDeviceIdentity) : ITerrainLabelRepository
{
    public async Task<List<TerrainLabel>> GetLiveAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.QueryAsync<TerrainLabel>("SELECT * FROM terrain_label WHERE deleted IS NULL");
    }

    public async Task InsertAsync(TerrainLabel label)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        await connectionContext.RunInTransactionAsync(connection =>
        {
            SynchronizationWriteMetadata.PrepareAuthoredWrite(connection, label, timeProvider.GetUtcNow().ToUnixTimeSeconds(), writer);
            connection.Insert(label);
        });
    }

    public Task<TerrainLabel?> SetClassAsync(Guid labelId, TerrainLabelClass labelClass) =>
        UpdateLiveAsync(labelId, (label, _) => label.LabelClass = labelClass);

    public Task<TerrainLabel?> DeleteAsync(Guid labelId) =>
        UpdateLiveAsync(labelId, (label, now) => label.Deleted = now);

    private async Task<TerrainLabel?> UpdateLiveAsync(Guid labelId, Action<TerrainLabel, long> change)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync(connection =>
        {
            var label = connection.Find<TerrainLabel>(labelId);
            if (label is null || label.Deleted is not null) return null;
            var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            change(label, now);
            SynchronizationWriteMetadata.PrepareAuthoredWrite(connection, label, now, writer);
            connection.Update(label);
            if (label.Deleted is not null) SynchronizationWriteMetadata.RecordDeletion(connection, label);
            return label;
        });
    }
}
