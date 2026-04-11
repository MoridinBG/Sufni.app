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

namespace Sufni.App.Coordinators;

public sealed class BikeCoordinator(
    IBikeStoreWriter bikeStore,
    IDatabaseService databaseService,
    IBikeDependencyQuery dependencyQuery,
    IShellCoordinator shell,
    IBikeEditorService bikeEditorService,
    IDialogService dialogService) : IBikeCoordinator
{
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
        var result = await bikeEditorService.ImportBikeAsync(cancellationToken);

        return result switch
        {
            BikeFileImportResult.Imported imported => await BuildImportedBikeResultAsync(imported.Bike, cancellationToken),
            BikeFileImportResult.Canceled => new BikeImportResult.Canceled(),
            BikeFileImportResult.InvalidFile invalid => new BikeImportResult.InvalidFile(invalid.ErrorMessage),
            BikeFileImportResult.Failed failed => new BikeImportResult.Failed(failed.ErrorMessage),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    public Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default) =>
        bikeEditorService.ExportBikeAsync(bike, cancellationToken);

    public async Task<BikeSaveResult> SaveAsync(Bike bike, long baselineUpdated)
    {
        var current = bikeStore.Get(bike.Id);
        if (current.IsNewerThan(baselineUpdated))
        {
            return new BikeSaveResult.Conflict(current);
        }

        BikeEditorAnalysisResult analysisResult = new BikeEditorAnalysisResult.Unavailable();
        if (bike.Linkage is not null)
        {
            analysisResult = await bikeEditorService.AnalyzeLinkageAsync(bike.Linkage);
            switch (analysisResult)
            {
                case BikeEditorAnalysisResult.Unavailable:
                    return new BikeSaveResult.InvalidLinkage();
                case BikeEditorAnalysisResult.Failed failed:
                    return new BikeSaveResult.Failed(failed.ErrorMessage);
            }
        }

        try
        {
            await databaseService.PutAsync(bike);
            var saved = BikeSnapshot.From(bike);
            bikeStore.Upsert(saved);

            return new BikeSaveResult.Saved(saved.Updated, analysisResult);
        }
        catch (Exception e)
        {
            return new BikeSaveResult.Failed(e.Message);
        }
    }

    public async Task<BikeDeleteResult> DeleteAsync(Guid bikeId)
    {
        if (await dependencyQuery.IsBikeInUseAsync(bikeId))
        {
            return new BikeDeleteResult(BikeDeleteOutcome.InUse);
        }

        try
        {
            await databaseService.DeleteAsync<Bike>(bikeId);
        }
        catch (Exception e)
        {
            return new BikeDeleteResult(BikeDeleteOutcome.Failed, e.Message);
        }

        shell.CloseIfOpen<BikeEditorViewModel>(editor => editor.Id == bikeId);
        bikeStore.Remove(bikeId);
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
