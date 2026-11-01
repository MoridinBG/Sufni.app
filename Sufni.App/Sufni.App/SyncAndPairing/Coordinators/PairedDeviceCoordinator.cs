using System;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Owns local paired-device removal.</summary>
public sealed class PairedDeviceCoordinator : IPairedDeviceCoordinator
{
    private static readonly ILogger logger = Log.ForContext<PairedDeviceCoordinator>();

    private readonly IPairedDeviceStoreWriter pairedDeviceStore;
    private readonly IPairedDeviceRepository repository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;

    public PairedDeviceCoordinator(
        IPairedDeviceStoreWriter pairedDeviceStore,
        IPairedDeviceRepository repository,
        IBackgroundTaskRunner backgroundTaskRunner)
    {
        this.pairedDeviceStore = pairedDeviceStore;
        this.repository = repository;
        this.backgroundTaskRunner = backgroundTaskRunner;

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

}

public abstract record PairedDeviceUnpairResult
{
    private PairedDeviceUnpairResult() { }

    public sealed record Unpaired : PairedDeviceUnpairResult;
    public sealed record Failed(PairedDeviceUnpairFailureReason Reason, string? Details = null) : PairedDeviceUnpairResult;
}
