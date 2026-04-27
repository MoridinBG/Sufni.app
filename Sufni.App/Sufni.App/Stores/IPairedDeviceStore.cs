using System;
using System.Threading.Tasks;
using DynamicData;

namespace Sufni.App.Stores;

/// <summary>
/// Read-only view of the paired-device collection. Injected into the
/// list view model. Mutations flow through
/// <see cref="IPairedDeviceStoreWriter"/> and are reserved for the
/// <see cref="Coordinators.PairedDeviceCoordinator"/> and the
/// composition root.
/// </summary>
public interface IPairedDeviceStore
{
    /// <summary>
    /// DynamicData change stream keyed by the <c>paired_device</c>
    /// primary key string. The list view model builds a row projection
    /// from this.
    /// </summary>
    IObservable<IChangeSet<PairedDeviceSnapshot, string>> Connect();

    /// <summary>
    /// Snapshot lookup by device id. Null if the device is not in the
    /// store.
    /// </summary>
    PairedDeviceSnapshot? Get(string deviceId);

    /// <summary>
    /// Load paired devices from the database and replace the current
    /// contents.
    /// </summary>
    Task RefreshAsync();
}
