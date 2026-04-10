using System;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Services;

namespace Sufni.App.Stores;

/// <summary>
/// Single source of truth for "what paired devices exist". Loaded from
/// the database via <see cref="RefreshAsync"/> and updated by the
/// <see cref="Coordinators.IPairedDeviceCoordinator"/> via
/// <see cref="IPairedDeviceStoreWriter"/>. Registered as a singleton
/// behind both <see cref="IPairedDeviceStore"/> and
/// <see cref="IPairedDeviceStoreWriter"/>.
/// </summary>
internal sealed class PairedDeviceStore(IDatabaseService databaseService) : IPairedDeviceStoreWriter
{
    private readonly SourceCache<PairedDeviceSnapshot, string> source = new(s => s.DeviceId);

    public IObservable<IChangeSet<PairedDeviceSnapshot, string>> Connect() => source.Connect();

    public PairedDeviceSnapshot? Get(string deviceId)
    {
        var lookup = source.Lookup(deviceId);
        return lookup.HasValue ? lookup.Value : null;
    }

    public async Task RefreshAsync()
    {
        var devices = await databaseService.GetPairedDevicesAsync();
        source.Edit(cache =>
        {
            cache.Clear();
            foreach (var device in devices)
            {
                cache.AddOrUpdate(PairedDeviceSnapshot.From(device));
            }
        });
    }

    public void Upsert(PairedDeviceSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(string deviceId) => source.RemoveKey(deviceId);
}
