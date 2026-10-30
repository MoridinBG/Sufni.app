using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>Shared paired-device snapshots, keyed by device identity.</summary>
internal sealed class PairedDeviceStore(StorePublication publication)
    : SourceCacheStoreBase<PairedDeviceSnapshot, string>(s => s.DeviceId, publication), IPairedDeviceStoreWriter
{
    public Task PublishPairedDevicesChangedAsync(IReadOnlyCollection<PairedDeviceSnapshot> snapshots) =>
        PublishSnapshotsAsync(snapshots);

    public Task PublishPairedDevicesRemovedAsync(IReadOnlyCollection<string> deviceIds) =>
        PublishRemovalsAsync(deviceIds);
}
