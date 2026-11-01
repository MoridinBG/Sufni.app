using System;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.SyncAndPairing.Services;

public interface ILocalDeviceIdentity
{
    Task<string> GetIdAsync();
}

/// <summary>Installation identity shared by pairing and authored sync writes.</summary>
internal sealed class LocalDeviceIdentity : ILocalDeviceIdentity
{
    private const string DeviceIdKey = "DeviceId";
    private readonly Lazy<Task<string>> identity;

    public LocalDeviceIdentity(ISecureStorage storage) => identity = new(() => LoadAsync(storage));

    public Task<string> GetIdAsync() => identity.Value;

    private static async Task<string> LoadAsync(ISecureStorage storage)
    {
        var stored = await storage.GetStringAsync(DeviceIdKey);
        if (stored is not null) return stored;
        var created = Guid.NewGuid().ToString();
        await storage.SetStringAsync(DeviceIdKey, created);
        return created;
    }
}
