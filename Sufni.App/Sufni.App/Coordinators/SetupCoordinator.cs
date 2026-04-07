using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

public sealed class SetupCoordinator(
    ISetupStoreWriter setupStore,
    IBikeStore bikeStore,
    IBikeCoordinator bikeCoordinator,
    IDatabaseService databaseService,
    IShellCoordinator shell,
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
            this,
            shell,
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

        shell.OpenOrFocus<SetupEditorViewModel>(
            editor => editor.Id == setupId,
            () => new SetupEditorViewModel(
                snapshot,
                isNew: false,
                bikeStore,
                bikeCoordinator,
                this,
                shell,
                dialogService));
        return Task.CompletedTask;
    }

    public async Task<SetupSaveResult> SaveAsync(Setup setup, Guid? boardId, long baselineUpdated)
    {
        // Optimistic conflict detection: if the store's current version
        // is newer than the baseline the editor opened on, someone else
        // (another tab, sync) has written in the meantime. For a brand
        // new setup the store has no entry, so this falls through.
        var current = setupStore.Get(setup.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            return new SetupSaveResult.Conflict(current);
        }

        try
        {
            // The previously stored board association — null for a brand
            // new setup or one that wasn't associated with a board.
            var originalBoardId = current?.BoardId;

            await databaseService.PutAsync(setup);

            // If this setup was already associated with another board, clear that association.
            // Do not delete the board though, it might be picked up later.
            if (originalBoardId.HasValue && originalBoardId != boardId)
            {
                await databaseService.PutAsync(new Board(originalBoardId.Value, null));
            }

            // If the board ID changed (or this is the first time we set
            // it), associate the new board with this setup.
            if (boardId.HasValue && originalBoardId != boardId)
            {
                await databaseService.PutAsync(new Board(boardId.Value, setup.Id));
            }

            var saved = SetupSnapshot.From(setup, boardId);
            setupStore.Upsert(saved);

            return new SetupSaveResult.Saved(saved.Updated);
        }
        catch (Exception e)
        {
            return new SetupSaveResult.Failed(e.Message);
        }
    }

    public async Task<SetupDeleteResult> DeleteAsync(Guid setupId)
    {
        var snapshot = setupStore.Get(setupId);

        try
        {
            await databaseService.DeleteAsync<Setup>(setupId);
        }
        catch (Exception e)
        {
            return new SetupDeleteResult(SetupDeleteOutcome.Failed, e.Message);
        }

        // Best-effort: clear the board association after the setup row
        // is gone. If this throws, the dangling board row is harmless —
        // setupStore.FindByBoardId will already return null for the
        // deleted setup.
        if (snapshot?.BoardId is not null)
        {
            try
            {
                await databaseService.PutAsync(new Board(snapshot.BoardId.Value, null));
            }
            catch
            {
                // Ignored; see comment above.
            }
        }

        // Close any open editor BEFORE removing the snapshot so no
        // editor binding observes a missing row mid-teardown.
        shell.CloseIfOpen<SetupEditorViewModel>(editor => editor.Id == setupId);
        setupStore.Remove(setupId);

        return new SetupDeleteResult(SetupDeleteOutcome.Deleted);
    }
}
