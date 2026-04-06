using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

public sealed class BikeCoordinator(
    IBikeStoreWriter bikeStore,
    IDatabaseService databaseService,
    IBikeDependencyQuery dependencyQuery,
    IShellCoordinator shell,
    IFilesService filesService,
    INavigator navigator,
    IDialogService dialogService) : IBikeCoordinator
{
    public Task OpenCreateAsync()
    {
        var seed = new Bike(Guid.NewGuid(), "new bike");
        var snapshot = BikeSnapshot.From(seed);
        var editor = new BikeEditorViewModel(
            snapshot,
            isNew: true,
            databaseService,
            filesService,
            navigator,
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

        var editor = new BikeEditorViewModel(
            snapshot,
            isNew: false,
            databaseService,
            filesService,
            navigator,
            dialogService);
        shell.Open(editor);
        return Task.CompletedTask;
    }

    public async Task<BikeSaveResult> SaveAsync(BikeEditorState state, long baselineUpdated)
    {
        // Optimistic conflict detection: if the store's current version
        // is newer than the baseline the editor opened on, someone else
        // (another tab, sync) has written in the meantime.
        if (!state.IsNew)
        {
            var current = bikeStore.Get(state.Id);
            if (current is not null && current.Updated > baselineUpdated)
            {
                return new BikeSaveResult(BikeSaveOutcome.ConflictDetected);
            }
        }

        try
        {
            var bike = new Bike(state.Id, state.Name)
            {
                HeadAngle = state.HeadAngle,
                ForkStroke = state.ForkStroke,
                Chainstay = state.Chainstay
            };

            if (state.ShockStroke is not null)
            {
                bike.Linkage = state.Linkage;
                bike.ShockStroke = state.ShockStroke;
                bike.Image = state.Image;
                bike.PixelsToMillimeters = state.PixelsToMillimeters;
            }

            await databaseService.PutAsync(bike);
            var saved = BikeSnapshot.From(bike);
            bikeStore.Upsert(saved);

            return new BikeSaveResult(BikeSaveOutcome.Saved, NewBaselineUpdated: saved.Updated);
        }
        catch (Exception e)
        {
            return new BikeSaveResult(BikeSaveOutcome.Failed, e.Message);
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
            bikeStore.Remove(bikeId);
            return new BikeDeleteResult(BikeDeleteOutcome.Deleted);
        }
        catch (Exception e)
        {
            return new BikeDeleteResult(BikeDeleteOutcome.Failed, e.Message);
        }
    }

    public async Task<bool> CanDeleteAsync(Guid bikeId) =>
        !await dependencyQuery.IsBikeInUseAsync(bikeId);
}
