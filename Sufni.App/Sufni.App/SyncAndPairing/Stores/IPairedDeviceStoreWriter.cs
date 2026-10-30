using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>Publication of prepared paired-device snapshots and removals.</summary>
public interface IPairedDeviceStoreWriter : IPairedDeviceStore
{
    Task ReplaceAllAsync(IReadOnlyList<PairedDeviceSnapshot> snapshots);
    Task PublishPairedDevicesChangedAsync(IReadOnlyCollection<PairedDeviceSnapshot> snapshots);
    Task PublishPairedDevicesRemovedAsync(IReadOnlyCollection<string> deviceIds);
}
