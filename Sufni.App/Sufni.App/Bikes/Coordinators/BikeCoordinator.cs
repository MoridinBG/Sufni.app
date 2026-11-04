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
    ISynchronizableRepository<Bike> bikeRepository,
    IBackgroundTaskRunner backgroundTaskRunner)
    : IBikeCoordinator
{
    private static readonly ILogger logger = Log.ForContext<BikeCoordinator>();
    private readonly object activeTasksGate = new();
    private readonly HashSet<Task> activeTasks = [];

    public async Task StopAsync()
    {
        Task[] tasks;
        lock (activeTasksGate) tasks = activeTasks.ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task ReleaseAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (activeTasksGate) activeTasks.Remove(task);
    }


    public async Task<BikeDraftLoadResult> LoadDraftAsync(Guid bikeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await backgroundTaskRunner.RunAsync<BikeDraftLoadResult>(async () =>
            {
                var bike = await bikeRepository.GetAsync(bikeId);
                return bike is null ? new BikeDraftLoadResult.Missing() : new BikeDraftLoadResult.Loaded(BikeDraft.From(bike));
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Bike draft could not be loaded for {BikeId}", bikeId);
            return new BikeDraftLoadResult.Failed(BikeLoadFailureReason.Read, exception.Message);
        }
    }

    public Task<BikeEditorAnalysisResult> LoadAnalysisAsync(
        RearSuspensionSpec rearSuspension,
        CancellationToken cancellationToken = default) =>
        bikeEditorService.LoadAnalysisAsync(rearSuspension, cancellationToken);

    public Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default) =>
        bikeEditorService.LoadImageAsync(cancellationToken);

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
                    _ => throw new ArgumentOutOfRangeException(),
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

    public Task<LeverageRatioImportResult> ImportLeverageRatioAsync(CancellationToken cancellationToken = default) =>
        bikeEditorService.ImportLeverageRatioAsync(cancellationToken);

    public async Task<BikeExportResult> ExportBikeAsync(BikeDraft draft, CancellationToken cancellationToken = default)
    {
        var current = bikeStore.Get(draft.Id);
        var bike = await backgroundTaskRunner.RunAsync(() => BuildRow(draft, current?.DampingSpeedCutoffs), cancellationToken);
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
            var result = await backgroundTaskRunner.RunAsync<BikeSaveResult>(async () =>
            {
                var current = await bikeRepository.GetAsync(draft.Id);
                if (draftBase is not null && current is null) return new BikeSaveResult.Missing();
                if (current is not null && (draftBase is null || BikeDraft.From(current) != draftBase))
                {
                    logger.Warning("Bike save conflict for {BikeId}", draft.Id);
                    return new BikeSaveResult.Conflict(BikeDraft.From(current));
                }

                var row = BuildRow(draft, current?.DampingSpeedCutoffs);
                await bikeRepository.PutAsync(row);
                return new BikeSaveResult.Saved(BikeSnapshot.From(row));
            });
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

    public Task<BikeDampingSpeedCutoffUpdateResult> UpdateDampingSpeedCutoffAsync(Guid bikeId, double expectedCutoffMmPerSecond, SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond)
    {
        lock (activeTasksGate)
        {
            var task = UpdateDampingSpeedCutoffCoreAsync(bikeId, expectedCutoffMmPerSecond, side, circuit, cutoffMmPerSecond);
            activeTasks.Add(task);
            _ = ReleaseAsync(task);
            return task;
        }
    }

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
            var result = await backgroundTaskRunner.RunAsync<BikeDampingSpeedCutoffUpdateResult>(async () =>
            {
                var current = await bikeRepository.GetAsync(bikeId);
                if (current is null) return new BikeDampingSpeedCutoffUpdateResult.Missing();
                if (current.DampingSpeedCutoffs.Get(side, circuit) != expectedCutoffMmPerSecond)
                    return new BikeDampingSpeedCutoffUpdateResult.Conflict(BikeSnapshot.From(current));

                var bike = Bike.FromSnapshot(BikeSnapshot.From(current), current.ImageBytes);
                bike.DampingSpeedCutoffs = bike.DampingSpeedCutoffs.With(side, circuit,
                    DampingCutoffEditing.RoundDragValue(cutoffMmPerSecond));
                await bikeRepository.PutAsync(bike);
                return new BikeDampingSpeedCutoffUpdateResult.Saved(BikeSnapshot.From(bike));
            });
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

    private static Bike BuildRow(BikeDraft draft, DampingSpeedCutoffs? currentCutoffs)
    {
        var row = Bike.FromSnapshot(draft.ToSnapshot(), draft.ImageBytes);
        row.DampingSpeedCutoffs = currentCutoffs ?? draft.InitialDampingSpeedCutoffs;
        return row;
    }


}

public abstract record BikeSaveResult
{
    private BikeSaveResult() { }

    public sealed record Saved(BikeSnapshot Snapshot) : BikeSaveResult;
    public sealed record Missing : BikeSaveResult;
    public sealed record Conflict(BikeDraft CurrentDraft) : BikeSaveResult;
    public sealed record Invalid(string ErrorMessage) : BikeSaveResult;
    public sealed record Failed(BikeSaveFailureReason Reason, string? Details = null) : BikeSaveResult;
}

public abstract record BikeDeleteResult
{
    private BikeDeleteResult() { }
    public sealed record Deleted : BikeDeleteResult;
    public sealed record InUse : BikeDeleteResult;
    public sealed record Failed(BikeDeleteFailureReason Reason, string? Details = null) : BikeDeleteResult;
}

public abstract record BikeDampingSpeedCutoffUpdateResult
{
    private BikeDampingSpeedCutoffUpdateResult() { }

    public sealed record Saved(BikeSnapshot Snapshot) : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Conflict(BikeSnapshot CurrentSnapshot) : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Missing : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Failed(BikeDampingCutoffFailureReason Reason, string? Details = null) : BikeDampingSpeedCutoffUpdateResult;
}
