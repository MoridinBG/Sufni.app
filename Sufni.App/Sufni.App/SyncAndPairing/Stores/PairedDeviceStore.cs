using System;
using System.Linq;
using System.Threading.Tasks;

using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Shared.Base;
namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>
/// Single source of truth for "what paired devices exist". Loaded from
/// the database via <see cref="RefreshAsync"/> and updated by the
/// <see cref="Coordinators.PairedDeviceCoordinator"/> via
/// <see cref="IPairedDeviceStoreWriter"/>. Registered as a singleton
/// behind both <see cref="IPairedDeviceStore"/> and
/// <see cref="IPairedDeviceStoreWriter"/>.
/// </summary>
internal sealed class PairedDeviceStore(IPairedDeviceRepository pairedDeviceRepository)
    : SourceCacheStoreBase<PairedDeviceSnapshot, string>(s => s.DeviceId), IPairedDeviceStoreWriter
{
    public async Task RefreshAsync()
    {
        var devices = await pairedDeviceRepository.GetPairedDevicesAsync();
        ReplaceWith(devices.Select(PairedDeviceSnapshot.From));
    }
}
