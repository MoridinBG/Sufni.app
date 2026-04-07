using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the paired-device feature workflow. Subscribes to the
/// synchronization server's <c>PairingConfirmed</c> and <c>Unpaired</c>
/// events in its constructor and keeps the
/// <see cref="IPairedDeviceStore"/> in sync. Registered as a singleton;
/// eagerly resolved at app startup so the constructor's event
/// subscriptions wire up before any pairing arrives.
/// </summary>
public sealed class PairedDeviceCoordinator : IPairedDeviceCoordinator
{
    private readonly IPairedDeviceStoreWriter pairedDeviceStore;
    private readonly IDatabaseService databaseService;

    public PairedDeviceCoordinator(
        IPairedDeviceStoreWriter pairedDeviceStore,
        IDatabaseService databaseService,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.pairedDeviceStore = pairedDeviceStore;
        this.databaseService = databaseService;

        if (synchronizationServer is not null)
        {
            synchronizationServer.PairingConfirmed += OnPairingConfirmed;
            synchronizationServer.Unpaired += OnUnpaired;
        }
    }

    public async Task<PairedDeviceUnpairResult> UnpairAsync(string deviceId)
    {
        try
        {
            await databaseService.DeletePairedDeviceAsync(deviceId);
            pairedDeviceStore.Remove(deviceId);
            return new PairedDeviceUnpairResult.Unpaired();
        }
        catch (Exception e)
        {
            return new PairedDeviceUnpairResult.Failed(e.Message);
        }
    }

    private void OnPairingConfirmed(object? sender, PairingEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            pairedDeviceStore.Upsert(PairedDeviceSnapshot.From(e.Device));
        });
    }

    private void OnUnpaired(object? sender, PairingEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            pairedDeviceStore.Remove(e.Device.DeviceId);
        });
    }
}
