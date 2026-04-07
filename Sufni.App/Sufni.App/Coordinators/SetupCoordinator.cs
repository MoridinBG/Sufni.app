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
    ITelemetryDataStoreService telemetryDataStoreService,
    IShellCoordinator shell,
    IDialogService dialogService) : ISetupCoordinator
{
    public Task OpenCreateAsync(Guid? suggestedBoardId = null)
    {
        // Honour the suggested board ID only if no other setup claims it.
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

    public Task OpenCreateForDetectedBoardAsync()
    {
        var detected = telemetryDataStoreService.DetectConnectedBoardId();
        return OpenCreateAsync(detected);
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
        var current = setupStore.Get(setup.Id);
        if (current.IsNewerThan(baselineUpdated))
        {
            return new SetupSaveResult.Conflict(current);
        }

        try
        {
            await databaseService.PutAsync(setup);
            await ReassignBoardAsync(current?.BoardId, boardId, setup.Id);

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

        // Best-effort: a dangling board row is harmless.
        try { await ReassignBoardAsync(snapshot?.BoardId, null, setupId); }
        catch { /* ignored */ }

        shell.CloseIfOpen<SetupEditorViewModel>(editor => editor.Id == setupId);
        setupStore.Remove(setupId);

        return new SetupDeleteResult(SetupDeleteOutcome.Deleted);
    }

    // Clear the previous board's setup pointer (if any) and set the new
    // board's setup pointer (if any). No-op when the assignment hasn't
    // changed. Boards are never deleted — they may be picked up later.
    private async Task ReassignBoardAsync(Guid? originalBoardId, Guid? newBoardId, Guid setupId)
    {
        if (originalBoardId == newBoardId) return;

        if (originalBoardId.HasValue)
        {
            await databaseService.PutAsync(new Board(originalBoardId.Value, null));
        }
        if (newBoardId.HasValue)
        {
            await databaseService.PutAsync(new Board(newBoardId.Value, setupId));
        }
    }
}
