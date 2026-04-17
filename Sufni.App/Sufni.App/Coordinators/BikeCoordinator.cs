using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.BikeEditing;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Kinematics;
using Serilog;

namespace Sufni.App.Coordinators;

public sealed class BikeCoordinator(
    IBikeStoreWriter bikeStore,
    IDatabaseService databaseService,
    IBikeDependencyQuery dependencyQuery,
    IShellCoordinator shell,
    IBikeEditorService bikeEditorService,
    IDialogService dialogService) : IBikeCoordinator
{
    private static readonly ILogger logger = Log.ForContext<BikeCoordinator>();

    public Task OpenCreateAsync()
    {
        var seed = new Bike(Guid.NewGuid(), "new bike");
        var snapshot = BikeSnapshot.From(seed);
        var editor = new BikeEditorViewModel(
            snapshot,
            isNew: true,
            this,
            dependencyQuery,
            shell,
            dialogService)
        {
            IsDirty = true
        };
        shell.Open(editor);
        return Task.CompletedTask;
    }

    public Task OpenEditAsync(Guid bikeId)
    {
        var snapshot = bikeStore.Get(bikeId);
        if (snapshot is null) return Task.CompletedTask;

        shell.OpenOrFocus<BikeEditorViewModel>(
            editor => editor.Id == bikeId,
            () => new BikeEditorViewModel(
                snapshot,
                isNew: false,
                this,
                dependencyQuery,
                shell,
                dialogService));
        return Task.CompletedTask;
    }

    public Task<BikeEditorAnalysisResult> LoadAnalysisAsync(
        Linkage? linkage,
        CancellationToken cancellationToken = default) =>
        bikeEditorService.AnalyzeLinkageAsync(linkage, cancellationToken);

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
        if (current.IsNewerThan(baselineUpdated))
        {
            logger.Warning("Bike save conflict for {BikeId}", bike.Id);
            return new BikeSaveResult.Conflict(current);
        }

        BikeEditorAnalysisResult analysisResult = new BikeEditorAnalysisResult.Unavailable();
        if (bike.Linkage is not null)
        {
            logger.Verbose("Analyzing linkage before bike save for {BikeId}", bike.Id);
            analysisResult = await bikeEditorService.AnalyzeLinkageAsync(bike.Linkage);
            switch (analysisResult)
            {
                case BikeEditorAnalysisResult.Unavailable:
                    logger.Warning("Bike save blocked because linkage was invalid for {BikeId}", bike.Id);
                    return new BikeSaveResult.InvalidLinkage();
                case BikeEditorAnalysisResult.Failed failed:
                    logger.Error(
                        "Bike save failed during linkage analysis for {BikeId}: {ErrorMessage}",
                        bike.Id,
                        failed.ErrorMessage);
                    return new BikeSaveResult.Failed(failed.ErrorMessage);
            }
        }

        try
        {
            await databaseService.PutAsync(bike);
            var saved = BikeSnapshot.From(bike);
            bikeStore.Upsert(saved);

            logger.Information("Bike save completed for {BikeId}", bike.Id);
            return new BikeSaveResult.Saved(saved.Updated, analysisResult);
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike save failed for {BikeId}", bike.Id);
            return new BikeSaveResult.Failed(e.Message);
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

        try
        {
            await databaseService.DeleteAsync<Bike>(bikeId);
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike delete failed for {BikeId}", bikeId);
            return new BikeDeleteResult(BikeDeleteOutcome.Failed, e.Message);
        }

        shell.CloseIfOpen<BikeEditorViewModel>(editor => editor.Id == bikeId);
        bikeStore.Remove(bikeId);
        logger.Information("Bike delete completed for {BikeId}", bikeId);
        return new BikeDeleteResult(BikeDeleteOutcome.Deleted);
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
        var analysis = await bikeEditorService.AnalyzeLinkageAsync(normalizedBike.Linkage, cancellationToken);
        return new BikeImportResult.Imported(new ImportedBikeEditorData(normalizedBike, analysis));
    }
}
