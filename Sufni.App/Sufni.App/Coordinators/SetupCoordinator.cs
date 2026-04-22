using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Serilog;

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
    private static readonly ILogger logger = Log.ForContext<SetupCoordinator>();

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

    public async Task OpenCreateForDetectedBoardAsync()
    {
        logger.Information("Starting setup create for detected board");
        var detected = await telemetryDataStoreService.DetectConnectedBoardIdAsync();
        logger.Information("Opening setup create for detected board {BoardId}", detected);
        await OpenCreateAsync(detected);
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
        logger.Information("Starting setup save for {SetupId}", setup.Id);

        var current = setupStore.Get(setup.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            logger.Warning("Setup save conflict for {SetupId}", setup.Id);
            return new SetupSaveResult.Conflict(current);
        }

        try
        {
            await databaseService.PutAsync(setup);

            if (current?.BoardId != boardId)
            {
                logger.Verbose(
                    "Reassigning setup board association for {SetupId} from {OriginalBoardId} to {NewBoardId}",
                    setup.Id,
                    current?.BoardId,
                    boardId);
            }

            await ReassignBoardAsync(current?.BoardId, boardId, setup.Id);

            var saved = SetupSnapshot.From(setup, boardId);
            setupStore.Upsert(saved);
            shell.GoBack();

            logger.Information("Setup save completed for {SetupId}", setup.Id);
            return new SetupSaveResult.Saved(saved.Updated);
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup save failed for {SetupId}", setup.Id);
            return new SetupSaveResult.Failed(e.Message);
        }
    }

    public async Task<SetupDeleteResult> DeleteAsync(Guid setupId)
    {
        logger.Information("Starting setup delete for {SetupId}", setupId);

        var snapshot = setupStore.Get(setupId);

        try
        {
            await databaseService.DeleteAsync<Setup>(setupId);
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup delete failed for {SetupId}", setupId);
            return new SetupDeleteResult(SetupDeleteOutcome.Failed, e.Message);
        }

        // Best-effort: a dangling board row is harmless.
        try { await ReassignBoardAsync(snapshot?.BoardId, null, setupId); }
        catch (Exception ex) { logger.Warning(ex, "Best-effort board reassign failed after setup delete"); }

        shell.CloseIfOpen<SetupEditorViewModel>(editor => editor.Id == setupId);
        setupStore.Remove(setupId);

        logger.Information("Setup delete completed for {SetupId}", setupId);
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
