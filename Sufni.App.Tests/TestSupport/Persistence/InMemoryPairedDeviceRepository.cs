using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemoryPairedDeviceRepository(params PairedDevice[] initial) : IPairedDeviceRepository
{
    private readonly Dictionary<string, PairedDevice> devices = initial.ToDictionary(device => device.DeviceId);
    public Exception? DeleteFailure { get; set; }

    public Task<List<PairedDevice>> GetPairedDevicesAsync() => Task.FromResult(devices.Values.ToList());
    public Task<PairedDevice?> GetPairedDeviceAsync(string id) => Task.FromResult(devices.GetValueOrDefault(id));
    public Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token) =>
        Task.FromResult(devices.Values.FirstOrDefault(device => device.Token == token));

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
