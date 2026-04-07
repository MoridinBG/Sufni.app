using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

public sealed class BikeCoordinator(
    IBikeStoreWriter bikeStore,
    IDatabaseService databaseService,
    IBikeDependencyQuery dependencyQuery,
    IShellCoordinator shell,
    IFilesService filesService,
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
            filesService,
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
                filesService,
                shell,
                dialogService));
        return Task.CompletedTask;
    }

    public async Task<BikeSaveResult> SaveAsync(Bike bike, long baselineUpdated)
    {
        // Optimistic conflict detection: if the store's current version
        // is newer than the baseline the editor opened on, someone else
        // (another tab, sync) has written in the meantime. For a brand
        // new bike the store has no entry, so this falls through.
        var current = bikeStore.Get(bike.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            return new BikeSaveResult.Conflict(current);
        }

        try
        {
            await databaseService.PutAsync(bike);
            var saved = BikeSnapshot.From(bike);
            bikeStore.Upsert(saved);

            return new BikeSaveResult.Saved(saved.Updated);
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

        // Close any open editor BEFORE removing the snapshot so no
        // editor binding observes a missing row mid-teardown.
        shell.CloseIfOpen<BikeEditorViewModel>(editor => editor.Id == bikeId);
        bikeStore.Remove(bikeId);
        return new BikeDeleteResult(BikeDeleteOutcome.Deleted);
    }

    public async Task<bool> CanDeleteAsync(Guid bikeId) =>
        !await dependencyQuery.IsBikeInUseAsync(bikeId);
}
