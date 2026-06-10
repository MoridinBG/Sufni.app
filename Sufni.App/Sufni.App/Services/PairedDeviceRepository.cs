using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface IPairedDeviceRepository
{
    Task<List<PairedDevice>> GetPairedDevicesAsync();

    Task<PairedDevice?> GetPairedDeviceAsync(string id);

    Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token);

    Task PutPairedDeviceAsync(PairedDevice device);

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

    public async Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<PairedDevice>()
            .Where(device => device.Token == token)
            .FirstOrDefaultAsync();
    }

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
