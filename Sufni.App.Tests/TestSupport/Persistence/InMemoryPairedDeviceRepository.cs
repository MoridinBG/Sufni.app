using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemoryPairedDeviceRepository(params PairedDevice[] initial) : IPairedDeviceRepository
{
    private readonly Dictionary<string, PairedDevice> devices = initial.ToDictionary(device => device.DeviceId);
    public Exception? DeleteFailure { get; set; }

    public Task<List<PairedDevice>> GetPairedDevicesAsync() => Task.FromResult(devices.Values.ToList());
    public Task<PairedDevice?> GetPairedDeviceAsync(string id) => Task.FromResult(devices.GetValueOrDefault(id));
    public Task<PairedDevice?> GetPairedDeviceByTokenHashAsync(string hash) =>
        Task.FromResult(devices.Values.FirstOrDefault(device => device.RefreshTokenHash == hash || device.PreviousRefreshTokenHash == hash));

    public Task<PairedDevice?> RotateRefreshTokenAsync(string deviceId, string presentedHash, string replacementHash, DateTime now, DateTime expires)
    {
        var device = devices.GetValueOrDefault(deviceId);
        if (device is null || device.Expires <= now ||
            (device.RefreshTokenHash != presentedHash && device.PreviousRefreshTokenHash != presentedHash)) return Task.FromResult<PairedDevice?>(null);
        device.PreviousRefreshTokenHash = device.RefreshTokenHash == presentedHash ? presentedHash : null;
        device.RefreshTokenHash = replacementHash;
        device.Expires = expires;
        return Task.FromResult<PairedDevice?>(device);
    }

    public Task PutPairedDeviceAsync(PairedDevice device)
    {
        devices[device.DeviceId] = device;
        return Task.CompletedTask;
    }

    public Task DeletePairedDeviceAsync(string id)
    {
        if (DeleteFailure is { } failure) return Task.FromException(failure);
        devices.Remove(id);
        return Task.CompletedTask;
    }
}
