using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;

namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>
/// Write surface for the paired-device store. Convention: only the
/// composition root and the
/// <see cref="Coordinators.PairedDeviceCoordinator"/> takes a
/// dependency on this interface. The list view model takes
/// <see cref="IPairedDeviceStore"/> instead.
/// </summary>
public interface IPairedDeviceStoreWriter : IPairedDeviceStore
{
    /// <summary>
    /// Load paired devices from the database and replace the current
    /// contents.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<PairedDeviceSnapshot> snapshots);

    Task<StoreDeleteResult<PairedDeviceSnapshot>> CommitLocalUnpairAsync(
        string deviceId);

    Task PublishPairedDevicesChangedAsync(
        IReadOnlyCollection<string> deviceIds,
        CancellationToken cancellationToken = default);

    Task PublishPairedDevicesRemovedAsync(
        IReadOnlyCollection<string> deviceIds,
        CancellationToken cancellationToken = default);
}
