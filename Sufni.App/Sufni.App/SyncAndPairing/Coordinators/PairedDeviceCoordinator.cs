using System;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Stores;
namespace Sufni.App.SyncAndPairing.Coordinators;

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
    private static readonly ILogger logger = Log.ForContext<PairedDeviceCoordinator>();

    private readonly IPairedDeviceStoreWriter pairedDeviceStore;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public PairedDeviceCoordinator(
        IPairedDeviceStoreWriter pairedDeviceStore,
        ISynchronizationServerService? synchronizationServer = null,
        IUiThreadDispatcher? uiThreadDispatcher = null)
    {
        this.pairedDeviceStore = pairedDeviceStore;
        this.uiThreadDispatcher = uiThreadDispatcher ?? new AvaloniaUiThreadDispatcher();

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
            var result = await pairedDeviceStore.CommitLocalUnpairAsync(deviceId);
            switch (result)
            {
                case StoreDeleteResult<PairedDeviceSnapshot>.Deleted:
                    logger.Information("Paired-device unpair completed for {DeviceId}", deviceId);
                    return new PairedDeviceUnpairResult.Unpaired();

                case StoreDeleteResult<PairedDeviceSnapshot>.Blocked blocked:
                    logger.Warning("Paired-device unpair blocked for {DeviceId}: {ErrorMessage}", deviceId, blocked.ErrorMessage);
                    return new PairedDeviceUnpairResult.Failed(blocked.ErrorMessage);

                case StoreDeleteResult<PairedDeviceSnapshot>.Missing missing:
                    logger.Warning("Paired-device unpair failed because {DeviceId} was missing: {ErrorMessage}", deviceId, missing.ErrorMessage);
                    return new PairedDeviceUnpairResult.Failed(missing.ErrorMessage);

                case StoreDeleteResult<PairedDeviceSnapshot>.Failed failed:
                    logger.Error("Paired-device unpair failed for {DeviceId}: {ErrorMessage}", deviceId, failed.ErrorMessage);
                    return new PairedDeviceUnpairResult.Failed(failed.ErrorMessage);

                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "Paired-device unpair failed for {DeviceId}", deviceId);
            return new PairedDeviceUnpairResult.Failed(e.Message);
        }
    }

    private void OnPairingConfirmed(object? sender, PairingEventArgs e)
    {
        logger.Verbose("Received inbound pairing confirmation for {DeviceId}", e.Device.DeviceId);
        _ = uiThreadDispatcher.InvokeAsync(() =>
        {
            return pairedDeviceStore.PublishPairedDevicesChangedAsync([e.Device.DeviceId]);
        });
    }

    private void OnUnpaired(object? sender, PairingEventArgs e)
    {
        logger.Verbose("Received inbound unpair for {DeviceId}", e.Device.DeviceId);
        _ = uiThreadDispatcher.InvokeAsync(() =>
        {
            return pairedDeviceStore.PublishPairedDevicesRemovedAsync([e.Device.DeviceId]);
        });
    }
}

public abstract record PairedDeviceUnpairResult
{
    private PairedDeviceUnpairResult() { }

    public sealed record Unpaired : PairedDeviceUnpairResult;
    public sealed record Failed(string ErrorMessage) : PairedDeviceUnpairResult;
}
