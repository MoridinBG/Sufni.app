using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;
using System.Threading.Tasks;
using System.Threading;
using System;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Shared.Stores;
namespace Sufni.App.Bikes.Coordinators;

internal class BikeCoordinator(
    IBikeStoreWriter bikeStore,
    IBikeDependencyQuery dependencyQuery,
    IShellCoordinator shell,
    IAppEnvironment appEnvironment,
    IBikeEditorService bikeEditorService,
    IBikeRearSuspensionValidator rearSuspensionValidator,
    Func<IEditorFactory> editorFactory)
    : IBikeCoordinator
{
    private static readonly ILogger logger = Log.ForContext<BikeCoordinator>();

    public Task OpenCreateAsync()
    {
        var seed = new Bike(Guid.NewGuid(), "new bike");
        var snapshot = BikeSnapshot.From(seed);
        editorFactory().OpenNewBikeEditor(snapshot);
        return Task.CompletedTask;
    }

    public Task OpenEditAsync(Guid bikeId)
    {
        var snapshot = bikeStore.Get(bikeId);
        if (snapshot is null) return Task.CompletedTask;

        editorFactory().OpenBikeEditor(snapshot);
        return Task.CompletedTask;
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
                logger.Verbose("Preparing imported bike editor data");
                var importedResult = await BuildImportedBikeResultAsync(imported.Bike, cancellationToken);
                logger.Information("Bike import completed");
                return importedResult;

            case BikeFileImportResult.Canceled:
                logger.Information("Bike import canceled");
                return new BikeImportResult.Canceled();

            case BikeFileImportResult.InvalidFile invalid:
                logger.Error("Bike import failed because the selected file was invalid: {ErrorMessage}", invalid.ErrorMessage);
                return new BikeImportResult.InvalidFile(invalid.ErrorMessage);

            case BikeFileImportResult.Failed failed:
                logger.Error("Bike import failed: {ErrorMessage}", failed.ErrorMessage);
                return new BikeImportResult.Failed(failed.ErrorMessage);

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    public Task<LeverageRatioImportResult> ImportLeverageRatioAsync(CancellationToken cancellationToken = default) =>
        bikeEditorService.ImportLeverageRatioAsync(cancellationToken);

    public async Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default)
    {
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

            case BikeExportResult.Failed failed:
                logger.Error("Bike export failed for {BikeId}: {ErrorMessage}", bike.Id, failed.ErrorMessage);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }

        return result;
    }

    public async Task<BikeSaveResult> SaveAsync(Bike bike, long baselineUpdated)
    {
        logger.Information("Starting bike save for {BikeId}", bike.Id);

        var current = bikeStore.Get(bike.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            logger.Warning("Bike save conflict for {BikeId}", bike.Id);
            return new BikeSaveResult.Conflict(current);
        }

        var validation = rearSuspensionValidator.ValidateForSave(BikeSnapshot.From(bike));
        if (validation is BikeRearSuspensionValidationResult.Invalid invalid)
        {
            var rearSuspensionError = RearSuspensionValidationMessages.ForSave(invalid.Failure);
            logger.Warning("Bike save blocked because the rear suspension was invalid for {BikeId}: {ErrorMessage}", bike.Id, rearSuspensionError);
            return new BikeSaveResult.InvalidRearSuspension(rearSuspensionError);
        }

        var validRearSuspension = (BikeRearSuspensionValidationResult.Valid)validation;
        BikeEditorAnalysisResult analysisResult = new BikeEditorAnalysisResult.Unavailable();
        switch (validRearSuspension.RearSuspension)
        {
            case RearSuspensionSpec.Hardtail:
                break;

            case RearSuspensionSpec.Linkage:
                logger.Verbose("Analyzing linkage before bike save for {BikeId}", bike.Id);
                analysisResult = await bikeEditorService.LoadAnalysisAsync(validRearSuspension.RearSuspension);
                switch (analysisResult)
                {
                    case BikeEditorAnalysisResult.Unavailable:
                        logger.Warning("Bike save blocked because linkage was invalid for {BikeId}", bike.Id);
                        return new BikeSaveResult.InvalidRearSuspension(RearSuspensionValidationMessages.ForSave(
                            new BikeRearSuspensionValidationFailure(BikeRearSuspensionValidationFailureCode.LinkageInvalidOrUnsolvable)));
                    case BikeEditorAnalysisResult.Failed failed:
                        logger.Error(
                            "Bike save failed during linkage analysis for {BikeId}: {ErrorMessage}",
                            bike.Id,
                            failed.ErrorMessage);
                        return new BikeSaveResult.Failed(failed.ErrorMessage);
                }
                break;

            case RearSuspensionSpec.LeverageRatio:
                logger.Verbose("Analyzing leverage ratio before bike save for {BikeId}", bike.Id);
                analysisResult = await bikeEditorService.LoadAnalysisAsync(validRearSuspension.RearSuspension);
                if (analysisResult is BikeEditorAnalysisResult.Failed leverageRatioFailed)
                {
                    logger.Error(
                        "Bike save failed during leverage ratio analysis for {BikeId}: {ErrorMessage}",
                        bike.Id,
                        leverageRatioFailed.ErrorMessage);
                    return new BikeSaveResult.Failed(leverageRatioFailed.ErrorMessage);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(validRearSuspension),
                    validRearSuspension.RearSuspension.GetType().Name);
        }

        try
        {
            var mutationResult = await bikeStore.CommitBikeAsync(bike, baselineUpdated);
            switch (mutationResult)
            {
                case StoreMutationResult<BikeSnapshot>.Saved saved:
                    if (appEnvironment.LayoutProfile == UiLayoutProfile.Compact)
                    {
                        _ = shell.GoBack();
                    }

                    logger.Information("Bike save completed for {BikeId}", bike.Id);
                    return new BikeSaveResult.Saved(saved.Snapshot.Updated, analysisResult);

                case StoreMutationResult<BikeSnapshot>.Conflict conflict:
                    logger.Warning("Bike save conflict for {BikeId}", bike.Id);
                    return new BikeSaveResult.Conflict(conflict.CurrentSnapshot);

                case StoreMutationResult<BikeSnapshot>.Missing missing:
                    logger.Error("Bike save failed because bike {BikeId} was missing: {ErrorMessage}", bike.Id, missing.ErrorMessage);
                    return new BikeSaveResult.Failed(missing.ErrorMessage);

                case StoreMutationResult<BikeSnapshot>.Failed failed:
                    logger.Error("Bike save failed for {BikeId}: {ErrorMessage}", bike.Id, failed.ErrorMessage);
                    return new BikeSaveResult.Failed(failed.ErrorMessage);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mutationResult));
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike save failed for {BikeId}", bike.Id);
            return new BikeSaveResult.Failed(e.Message);
        }
    }

    public async Task<BikeDampingSpeedCutoffUpdateResult> UpdateDampingSpeedCutoffAsync(
        Guid bikeId,
        long baselineUpdated,
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        logger.Information(
            "Starting bike damping speed cutoff update for {BikeId} {Side} {Circuit}",
            bikeId,
            side,
            circuit);

        var current = bikeStore.Get(bikeId);
        if (current is null)
        {
            logger.Warning("Bike damping speed cutoff update failed because bike {BikeId} was not found", bikeId);
            return new BikeDampingSpeedCutoffUpdateResult.Failed("Bike was not found.");
        }

        if (current.Updated > baselineUpdated)
        {
            logger.Warning("Bike damping speed cutoff update conflict for {BikeId}", bikeId);
            return new BikeDampingSpeedCutoffUpdateResult.Conflict(current);
        }

        var roundedCutoff = DampingCutoffEditing.RoundDragValue(cutoffMmPerSecond);
        var updatedCutoffs = current.DampingSpeedCutoffs.With(side, circuit, roundedCutoff);
        var updatedSnapshot = current with
        {
            FrontCompressionDampingCutoffMmPerSecond = updatedCutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = updatedCutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = updatedCutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = updatedCutoffs.Rear.ReboundMmPerSecond,
        };
        var bike = Bike.FromSnapshot(updatedSnapshot);

        try
        {
            var mutationResult = await bikeStore.CommitBikeAsync(bike, baselineUpdated);
            switch (mutationResult)
            {
                case StoreMutationResult<BikeSnapshot>.Saved saved:
                    logger.Information("Bike damping speed cutoff update completed for {BikeId}", bikeId);
                    return new BikeDampingSpeedCutoffUpdateResult.Saved(saved.Snapshot);

                case StoreMutationResult<BikeSnapshot>.Conflict conflict:
                    logger.Warning("Bike damping speed cutoff update conflict for {BikeId}", bikeId);
                    return new BikeDampingSpeedCutoffUpdateResult.Conflict(conflict.CurrentSnapshot);

                case StoreMutationResult<BikeSnapshot>.Missing missing:
                    logger.Error(
                        "Bike damping speed cutoff update failed because bike {BikeId} was missing: {ErrorMessage}",
                        bikeId,
                        missing.ErrorMessage);
                    return new BikeDampingSpeedCutoffUpdateResult.Failed(missing.ErrorMessage);

                case StoreMutationResult<BikeSnapshot>.Failed failed:
                    logger.Error(
                        "Bike damping speed cutoff update failed for {BikeId}: {ErrorMessage}",
                        bikeId,
                        failed.ErrorMessage);
                    return new BikeDampingSpeedCutoffUpdateResult.Failed(failed.ErrorMessage);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mutationResult));
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike damping speed cutoff update failed for {BikeId}", bikeId);
            return new BikeDampingSpeedCutoffUpdateResult.Failed(e.Message);
        }
    }

    public async Task<BikeDeleteResult> DeleteAsync(Guid bikeId)
    {
        logger.Information("Starting bike delete for {BikeId}", bikeId);

        if (await dependencyQuery.IsBikeInUseAsync(bikeId))
        {
            logger.Warning("Bike delete blocked because bike {BikeId} is in use", bikeId);
            return new BikeDeleteResult(BikeDeleteOutcome.InUse);
        }

        var deleteResult = await bikeStore.CommitBikeDeleteAsync(bikeId);
        switch (deleteResult)
        {
            case StoreDeleteResult<BikeSnapshot>.Deleted:
                await editorFactory().CloseBikeEditor(bikeId);
                logger.Information("Bike delete completed for {BikeId}", bikeId);
                return new BikeDeleteResult(BikeDeleteOutcome.Deleted);

            case StoreDeleteResult<BikeSnapshot>.Blocked blocked:
                logger.Warning("Bike delete blocked for {BikeId}: {ErrorMessage}", bikeId, blocked.ErrorMessage);
                return new BikeDeleteResult(BikeDeleteOutcome.Failed, blocked.ErrorMessage);

            case StoreDeleteResult<BikeSnapshot>.Missing missing:
                logger.Warning("Bike delete failed because bike {BikeId} was missing: {ErrorMessage}", bikeId, missing.ErrorMessage);
                return new BikeDeleteResult(BikeDeleteOutcome.Failed, missing.ErrorMessage);

            case StoreDeleteResult<BikeSnapshot>.Failed failed:
                logger.Error("Bike delete failed for {BikeId}: {ErrorMessage}", bikeId, failed.ErrorMessage);
                return new BikeDeleteResult(BikeDeleteOutcome.Failed, failed.ErrorMessage);

            default:
                throw new ArgumentOutOfRangeException(nameof(deleteResult));
        }
    }

    private static Bike NormalizeImportedBike(Bike imported)
    {
        var snapshot = BikeSnapshot.From(imported) with
        {
            Id = Guid.NewGuid(),
            Updated = 0,
        };
        var normalized = Bike.FromSnapshot(snapshot);

        normalized.ClientUpdated = 0;
        normalized.Deleted = null;

        return normalized;
    }

    private async Task<BikeImportResult.Imported> BuildImportedBikeResultAsync(
        Bike imported,
        CancellationToken cancellationToken)
    {
        var normalizedBike = NormalizeImportedBike(imported);
        var analysis = await bikeEditorService.LoadAnalysisAsync(normalizedBike.RearSuspension, cancellationToken);

        return new BikeImportResult.Imported(new ImportedBikeEditorData(normalizedBike, analysis));
    }
}

public abstract record BikeSaveResult
{
    private BikeSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated, BikeEditorAnalysisResult AnalysisResult) : BikeSaveResult;
    public sealed record Conflict(BikeSnapshot CurrentSnapshot) : BikeSaveResult;
    public sealed record InvalidRearSuspension(string ErrorMessage) : BikeSaveResult;
    public sealed record Failed(string ErrorMessage) : BikeSaveResult;
}

public sealed record BikeDeleteResult(BikeDeleteOutcome Outcome, string? ErrorMessage = null);

public abstract record BikeDampingSpeedCutoffUpdateResult
{
    private BikeDampingSpeedCutoffUpdateResult() { }

    public sealed record Saved(BikeSnapshot Snapshot) : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Conflict(BikeSnapshot CurrentSnapshot) : BikeDampingSpeedCutoffUpdateResult;
    public sealed record Failed(string ErrorMessage) : BikeDampingSpeedCutoffUpdateResult;
}

public enum BikeDeleteOutcome
{
    Deleted,
    InUse,
    Failed
}
