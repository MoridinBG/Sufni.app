using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Shared.Stores;
namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>
/// Single source of truth for "what paired devices exist". Loaded from
/// the database via <see cref="RefreshAsync"/> and updated by the
/// <see cref="Coordinators.PairedDeviceCoordinator"/> via
/// <see cref="IPairedDeviceStoreWriter"/>. Registered as a singleton
/// behind both <see cref="IPairedDeviceStore"/> and
/// <see cref="IPairedDeviceStoreWriter"/>.
/// </summary>
internal sealed class PairedDeviceStore(
    IPairedDeviceRepository pairedDeviceRepository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<PairedDeviceSnapshot, string>(s => s.DeviceId, uiThreadDispatcher), IPairedDeviceStoreWriter
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = await pairedDeviceRepository.GetPairedDevicesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(devices.Select(PairedDeviceSnapshot.From));
    }

    public async Task<StoreDeleteResult<PairedDeviceSnapshot>> CommitLocalUnpairAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = Get(deviceId);

        try
        {
            await pairedDeviceRepository.DeletePairedDeviceAsync(deviceId);
            cancellationToken.ThrowIfCancellationRequested();
            await PublishRemoveAsync(deviceId);
            return new StoreDeleteResult<PairedDeviceSnapshot>.Deleted(previous);
        }
        catch (Exception e)
        {
            return new StoreDeleteResult<PairedDeviceSnapshot>.Failed(e.Message);
        }
    }

    public async Task PublishPairedDevicesChangedAsync(
        IReadOnlyCollection<string> deviceIds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<PairedDeviceSnapshot>();
        var removedIds = new List<string>();

        foreach (var deviceId in deviceIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = await pairedDeviceRepository.GetPairedDeviceAsync(deviceId);
            if (device is null)
            {
                removedIds.Add(deviceId);
            }
            else
            {
                snapshots.Add(PairedDeviceSnapshot.From(device));
            }
        }

        if (snapshots.Count > 0)
        {
            await PublishSnapshotsAsync(snapshots);
        }

        if (removedIds.Count > 0)
        {
            await PublishRemovalsAsync(removedIds);
        }
    }

    public Task PublishPairedDevicesRemovedAsync(
        IReadOnlyCollection<string> deviceIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(deviceIds.Distinct());
    }
}
