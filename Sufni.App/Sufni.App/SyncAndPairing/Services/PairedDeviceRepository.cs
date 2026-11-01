using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Services;

public interface IPairedDeviceRepository
{
    Task<List<PairedDevice>> GetPairedDevicesAsync();

    Task<PairedDevice?> GetPairedDeviceAsync(string id);

    Task<PairedDevice?> GetPairedDeviceByTokenHashAsync(string hash);

    Task PutPairedDeviceAsync(PairedDevice device);

    Task<PairedDevice?> RotateRefreshTokenAsync(string deviceId, string presentedHash, string replacementHash, DateTime now, DateTime expires);

    Task DeletePairedDeviceAsync(string id);
}

internal sealed class PairedDeviceRepository(SqliteConnectionContext connectionContext) : IPairedDeviceRepository
{
    public async Task<List<PairedDevice>> GetPairedDevicesAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<PairedDevice>().ToListAsync();
    }

    public async Task<PairedDevice?> GetPairedDeviceAsync(string id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<PairedDevice>()
            .Where(device => device.DeviceId == id)
            .FirstOrDefaultAsync();
    }

    public async Task<PairedDevice?> GetPairedDeviceByTokenHashAsync(string hash)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<PairedDevice>()
            .Where(device => device.RefreshTokenHash == hash || device.PreviousRefreshTokenHash == hash)
            .FirstOrDefaultAsync();
    }

    public Task<PairedDevice?> RotateRefreshTokenAsync(string deviceId, string presentedHash, string replacementHash, DateTime now, DateTime expires) =>
        connectionContext.RunInTransactionAsync<PairedDevice?>(connection =>
        {
            var device = connection.Find<PairedDevice>(deviceId);
            if (device is null || device.Expires <= now ||
                (device.RefreshTokenHash != presentedHash && device.PreviousRefreshTokenHash != presentedHash)) return null;
            device.PreviousRefreshTokenHash = device.RefreshTokenHash == presentedHash ? device.RefreshTokenHash : null;
            device.RefreshTokenHash = replacementHash;
            device.Expires = expires;
            connection.Update(device);
            return device;
        });

    public async Task PutPairedDeviceAsync(PairedDevice device)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var existing = await connection.Table<PairedDevice>()
            .Where(storedDevice => storedDevice.DeviceId == device.DeviceId)
            .FirstOrDefaultAsync() is not null;
        if (existing)
        {
            await connection.UpdateAsync(device);
        }
        else
        {
            await connection.InsertAsync(device);
        }
    }

    public async Task DeletePairedDeviceAsync(string id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var device = await connection.Table<PairedDevice>()
            .Where(storedDevice => storedDevice.DeviceId == id)
            .FirstOrDefaultAsync();
        if (device is not null)
        {
            await connection.DeleteAsync(device);
        }
    }
}
