using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.SyncAndPairing.Stores;

/// <summary>Shared paired-device snapshots, keyed by device identity.</summary>
internal sealed class PairedDeviceStore(
    IPairedDeviceRepository pairedDeviceRepository,
    StorePublication publication)
    : SourceCacheStoreBase<PairedDeviceSnapshot, string>(s => s.DeviceId, publication), IPairedDeviceStoreWriter
{
    public async Task<StoreDeleteResult<PairedDeviceSnapshot>> CommitLocalUnpairAsync(
        string deviceId)
    {
        var previous = Get(deviceId);

        try
        {
            await pairedDeviceRepository.DeletePairedDeviceAsync(deviceId);
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
