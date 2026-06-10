using System.Threading.Tasks;

namespace Sufni.App.Stores;

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
    Task RefreshAsync();

    /// <summary>
    /// Insert or replace the snapshot for a paired device. Called by
    /// the coordinator on <c>PairingConfirmed</c> and after a refresh.
    /// </summary>
    void Upsert(PairedDeviceSnapshot snapshot);

    /// <summary>
    /// Remove a paired device from the store by id. No-op if not present.
    /// </summary>
    void Remove(string deviceId);
}
