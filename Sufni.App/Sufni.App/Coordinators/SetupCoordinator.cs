using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

public sealed class SetupCoordinator(
    ISetupStoreWriter setupStore,
    IBikeStore bikeStore,
    IBikeCoordinator bikeCoordinator,
    IDatabaseService databaseService,
    IShellCoordinator shell,
    INavigator navigator,
    IDialogService dialogService) : ISetupCoordinator
{
    public Task OpenCreateAsync(Guid? suggestedBoardId = null)
    {
        // Honour the suggested board ID only if it isn't already
        // associated with another setup. This is the check that used
        // to live in SetupListViewModel.AddImplementation.
        Guid? actualBoardId = null;
        if (suggestedBoardId.HasValue && setupStore.FindByBoardId(suggestedBoardId.Value) is null)
        {
            actualBoardId = suggestedBoardId;
        }

        var seed = new Setup(Guid.NewGuid(), "new setup");
        var snapshot = SetupSnapshot.From(seed, actualBoardId);
        var editor = new SetupEditorViewModel(
            snapshot,
            isNew: true,
            bikeStore,
            bikeCoordinator,
            databaseService,
            navigator,
            dialogService)
        {
            IsDirty = true
        };
        shell.Open(editor);
        return Task.CompletedTask;
    }

    public Task OpenEditAsync(Guid setupId)
    {
        var snapshot = setupStore.Get(setupId);
        if (snapshot is null) return Task.CompletedTask;

        var editor = new SetupEditorViewModel(
            snapshot,
            isNew: false,
            bikeStore,
            bikeCoordinator,
            databaseService,
            navigator,
            dialogService);
        shell.Open(editor);
        return Task.CompletedTask;
    }

    public async Task<SetupSaveResult> SaveAsync(SetupEditorState state, long baselineUpdated)
    {
        // Optimistic conflict detection: if the store's current version
        // is newer than the baseline the editor opened on, someone else
        // (another tab, sync) has written in the meantime.
        if (!state.IsNew)
        {
            var current = setupStore.Get(state.Id);
            if (current is not null && current.Updated > baselineUpdated)
            {
                return new SetupSaveResult(SetupSaveOutcome.ConflictDetected);
            }
        }

        try
        {
            var setup = new Setup(state.Id, state.Name)
            {
                BikeId = state.BikeId,
                FrontSensorConfigurationJson = state.FrontSensorConfigurationJson,
                RearSensorConfigurationJson = state.RearSensorConfigurationJson
            };
            await databaseService.PutAsync(setup);

            // If this setup was already associated with another board, clear that association.
            // Do not delete the board though, it might be picked up later.
            if (state.OriginalBoardId.HasValue && !state.IsNew && state.OriginalBoardId != state.BoardId)
            {
                await databaseService.PutAsync(new Board(state.OriginalBoardId.Value, null));
            }

            // If the board ID changed, or this is a new setup, associate it with the board ID.
            if (state.BoardId.HasValue && (state.IsNew || state.OriginalBoardId != state.BoardId))
            {
                await databaseService.PutAsync(new Board(state.BoardId.Value, setup.Id));
            }

            var saved = SetupSnapshot.From(setup, state.BoardId);
            setupStore.Upsert(saved);

            return new SetupSaveResult(SetupSaveOutcome.Saved, NewBaselineUpdated: saved.Updated);
        }
        catch (Exception e)
        {
            return new SetupSaveResult(SetupSaveOutcome.Failed, e.Message);
        }
    }

    public async Task<SetupDeleteResult> DeleteAsync(Guid setupId)
    {
        try
        {
            var snapshot = setupStore.Get(setupId);

            // If this setup is associated with a board ID, clear that association.
            if (snapshot?.BoardId is not null)
            {
                await databaseService.PutAsync(new Board(snapshot.BoardId.Value, null));
            }

            await databaseService.DeleteAsync<Setup>(setupId);
            setupStore.Remove(setupId);

            return new SetupDeleteResult(SetupDeleteOutcome.Deleted);
        }
        catch (Exception e)
        {
            return new SetupDeleteResult(SetupDeleteOutcome.Failed, e.Message);
        }
    }
}
