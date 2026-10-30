using System;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>
/// Owns the paired-device feature workflow. Subscribes to the
/// synchronization server's <c>PairingConfirmed</c> and <c>Unpaired</c>
/// events in its constructor and keeps the
/// <see cref="IPairedDeviceStore"/> in sync. Registered as a singleton;
/// eagerly resolved at app startup so the constructor's event
/// subscriptions wire up before any pairing arrives.
/// </summary>
public sealed class PairedDeviceCoordinator : IPairedDeviceCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<PairedDeviceCoordinator>();

    private readonly IPairedDeviceStoreWriter pairedDeviceStore;
    private readonly ISynchronizationServerService? server;
    private readonly IPairedDeviceRepository repository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;

    public PairedDeviceCoordinator(
        IPairedDeviceStoreWriter pairedDeviceStore,
        IPairedDeviceRepository repository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.pairedDeviceStore = pairedDeviceStore;
        server = synchronizationServer;
        this.repository = repository;
        this.backgroundTaskRunner = backgroundTaskRunner;

        if (synchronizationServer is not null)
        {
            synchronizationServer.PairingConfirmed += OnPairingConfirmed;
            synchronizationServer.Unpaired += OnUnpaired;
        }
    }

    public async Task<PairedDeviceUnpairResult> UnpairAsync(string deviceId)
    {
        logger.Information("Starting paired-device unpair for {DeviceId}", deviceId);

        try
        {
            await backgroundTaskRunner.RunAsync(() => repository.DeletePairedDeviceAsync(deviceId));
            await pairedDeviceStore.PublishPairedDevicesRemovedAsync([deviceId]);
            logger.Information("Paired-device unpair completed for {DeviceId}", deviceId);
            return new PairedDeviceUnpairResult.Unpaired();
        }
        catch (Exception e)
        {
            logger.Error(e, "Paired-device unpair failed for {DeviceId}", deviceId);
            return new PairedDeviceUnpairResult.Failed(PairedDeviceUnpairFailureReason.Unpair, e.Message);
        }
    }

    public void Dispose()
    {
        if (server is null) return;
        server.PairingConfirmed -= OnPairingConfirmed;
        server.Unpaired -= OnUnpaired;
    }

    private void OnPairingConfirmed(object? sender, PairingEventArgs e)
    {
        logger.Verbose("Received inbound pairing confirmation for {DeviceId}", e.Device.DeviceId);
        _ = pairedDeviceStore.PublishPairedDevicesChangedAsync([PairedDeviceSnapshot.From(e.Device)]);
    }

    private void OnUnpaired(object? sender, PairingEventArgs e)
    {
        logger.Verbose("Received inbound unpair for {DeviceId}", e.Device.DeviceId);
        _ = pairedDeviceStore.PublishPairedDevicesRemovedAsync([e.Device.DeviceId]);
    }
}

public abstract record PairedDeviceUnpairResult
{
    private PairedDeviceUnpairResult() { }

    public sealed record Unpaired : PairedDeviceUnpairResult;
    public sealed record Failed(PairedDeviceUnpairFailureReason Reason, string? Details = null) : PairedDeviceUnpairResult;
}
