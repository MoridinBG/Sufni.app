using Sufni.App.Shared.Common;
using System.Linq;
using System.Collections.Generic;
using Serilog;
using Sufni.Telemetry;
using System.Threading.Tasks;
using System.Threading;
using System;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Bikes.Coordinators;

internal class BikeCoordinator(
    IBikeStoreWriter bikeStore,
    IBikePersistenceTransactionRunner bikePersistenceTransactions,
    IBikeEditorService bikeEditorService,
    IBikeSaveValidator saveValidator,
    IBackgroundTaskRunner backgroundTaskRunner)
    : IBikeCoordinator
{
    private static readonly ILogger logger = Log.ForContext<BikeCoordinator>();
    private readonly RetainedWork retainedWork = new();

    public Task StopAsync() => retainedWork.DrainAsync();


    public async Task<BikeImportResult> ImportBikeAsync(CancellationToken cancellationToken = default)
    {
        logger.Information("Starting bike import");

        var result = await bikeEditorService.ImportBikeAsync(cancellationToken);

        switch (result)
        {
            case BikeFileImportResult.Imported imported:
                var draft = await backgroundTaskRunner.RunAsync(() => BikeDraft.From(imported.Bike) with { Id = Guid.NewGuid() });
                return await saveValidator.ValidateAsync(draft) switch
                {
                    BikeSaveValidation.Valid => new BikeImportResult.Imported(draft),
                    BikeSaveValidation.Invalid invalid => new BikeImportResult.InvalidFile(invalid.Message),
                    BikeSaveValidation.Failed failed => new BikeImportResult.Failed(BikeImportFailureReason.Import, failed.Details),
                };

            case BikeFileImportResult.Canceled:
                logger.Information("Bike import canceled");
                return new BikeImportResult.Canceled();

            case BikeFileImportResult.InvalidFile invalid:
                return new BikeImportResult.InvalidFile(invalid.ErrorMessage);

            case BikeFileImportResult.Failed failed:
                return new BikeImportResult.Failed(failed.Reason, failed.Details);

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    public async Task<BikeExportResult> ExportBikeAsync(BikeDraft draft, CancellationToken cancellationToken = default)
    {
        var current = bikeStore.Get(draft.Id);
        var bike = await backgroundTaskRunner.RunAsync(() => draft.ToRow(current?.DampingSpeedCutoffs), cancellationToken);
        logger.Information("Starting bike export for {BikeId}", bike.Id);

        var result = await bikeEditorService.ExportBikeAsync(bike, cancellationToken);

        switch (result)
        {
            case BikeExportResult.Exported:
                logger.Information("Bike export completed for {BikeId}", bike.Id);
                break;

            case BikeExportResult.Canceled:
                logger.Information("Bike export canceled for {BikeId}", bike.Id);
                break;

            case BikeExportResult.Failed:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }

        return result;
    }

    public async Task<BikeSaveResult> SaveAsync(BikeDraft draft, BikeDraft? draftBase)
    {
        logger.Information("Starting bike save for {BikeId}", draft.Id);
        switch (await saveValidator.ValidateAsync(draft))
        {
            case BikeSaveValidation.Invalid invalid:
                return new BikeSaveResult.Invalid(invalid.Message);
            case BikeSaveValidation.Failed failed:
                return new BikeSaveResult.Failed(BikeSaveFailureReason.Analysis, failed.Details);
        }

        try
        {
            var result = await bikePersistenceTransactions.SaveAsync(draft, draftBase);
            if (result is BikeSaveResult.Saved saved)
            {
                await bikeStore.PublishBikesChangedAsync([saved.Snapshot]);
                logger.Information("Bike save completed for {BikeId}", draft.Id);
            }
            return result;
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike save failed for {BikeId}", draft.Id);
            return new BikeSaveResult.Failed(BikeSaveFailureReason.Save, e.Message);
        }
    }

    public Task<BikeDampingSpeedCutoffUpdateResult> UpdateDampingSpeedCutoffAsync(Guid bikeId, double expectedCutoffMmPerSecond, SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) =>
        retainedWork.Retain(UpdateDampingSpeedCutoffCoreAsync(bikeId, expectedCutoffMmPerSecond, side, circuit, cutoffMmPerSecond));

    private async Task<BikeDampingSpeedCutoffUpdateResult> UpdateDampingSpeedCutoffCoreAsync(
        Guid bikeId,
        double expectedCutoffMmPerSecond,
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        logger.Information(
            "Starting bike damping speed cutoff update for {BikeId} {Side} {Circuit}",
            bikeId,
            side,
            circuit);

        try
        {
            var result = await bikePersistenceTransactions.UpdateCutoffAsync(
                bikeId, expectedCutoffMmPerSecond, side, circuit, cutoffMmPerSecond);
            if (result is BikeDampingSpeedCutoffUpdateResult.Saved saved)
            {
                await bikeStore.PublishBikesChangedAsync([saved.Snapshot]);
                logger.Information("Bike damping speed cutoff update completed for {BikeId}", bikeId);
            }
            return result;
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike damping speed cutoff update failed for {BikeId}", bikeId);
            return new BikeDampingSpeedCutoffUpdateResult.Failed(BikeDampingCutoffFailureReason.Save, e.Message);
        }
    }

    public async Task<BikeDeleteResult> DeleteAsync(Guid bikeId)
    {
        logger.Information("Starting bike delete for {BikeId}", bikeId);

        try
        {
            if (await bikePersistenceTransactions.DeleteBikeAsync(bikeId) is BikeDeletePersistenceResult.InUse)
            {
                logger.Warning("Bike delete blocked because bike {BikeId} is in use", bikeId);
                return new BikeDeleteResult.InUse();
            }

            await bikeStore.PublishBikesRemovedAsync([bikeId]);
            logger.Information("Bike delete completed for {BikeId}", bikeId);
            return new BikeDeleteResult.Deleted();
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike delete failed for {BikeId}", bikeId);
            return new BikeDeleteResult.Failed(BikeDeleteFailureReason.Delete, e.Message);
        }
    }


}

public closed record BikeSaveResult
{
    private BikeSaveResult() { }

    public sealed record Saved(BikeSnapshot Snapshot) : BikeSaveResult;
    public sealed record Missing : BikeSaveResult;
    public sealed record Conflict(BikeDraft CurrentDraft) : BikeSaveResult;
    public sealed record Invalid(string ErrorMessage) : BikeSaveResult;
    public sealed record Failed(BikeSaveFailureReason Reason, string? Details = null) : BikeSaveResult;
}

public closed record BikeDeleteResult
{
    private BikeDeleteResult() { }
    public sealed record Deleted : BikeDeleteResult;
    public sealed record InUse : BikeDeleteResult;
    public sealed record Failed(BikeDeleteFailureReason Reason, string? Details = null) : BikeDeleteResult;
}

public closed record BikeDampingSpeedCutoffUpdateResult
{
    private BikeDampingSpeedCutoffUpdateResult() { }

    public sealed record Saved(BikeSnapshot Snapshot) : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Conflict(BikeSnapshot CurrentSnapshot) : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Missing : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Failed(BikeDampingCutoffFailureReason Reason, string? Details = null) : BikeDampingSpeedCutoffUpdateResult;
}
