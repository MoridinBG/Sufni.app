using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.SetupEditing;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Serilog;

namespace Sufni.App.Coordinators;

public class SetupCoordinator(
    ISetupStoreWriter setupStore,
    IBikeStoreWriter bikeStore,
    BikeCoordinator bikeCoordinator,
    IDatabaseService databaseService,
    ITelemetryDataStoreService telemetryDataStoreService,
    IFilesService filesService,
    IBackgroundTaskRunner backgroundTaskRunner,
    IShellCoordinator shell,
    IDialogService dialogService)
{
    private static readonly ILogger logger = Log.ForContext<SetupCoordinator>();

    public virtual Task OpenCreateAsync(Guid? suggestedBoardId = null)
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

    public virtual async Task OpenCreateForDetectedBoardAsync()
    {
        logger.Information("Starting setup create for detected board");
        var detected = await telemetryDataStoreService.DetectConnectedBoardIdAsync();
        logger.Information("Opening setup create for detected board {BoardId}", detected);
        await OpenCreateAsync(detected);
    }

    public virtual Task OpenEditAsync(Guid setupId)
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

    public virtual async Task<SetupSaveResult> SaveAsync(Setup setup, Guid? boardId, long baselineUpdated)
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

    public virtual async Task<SetupDeleteResult> DeleteAsync(Guid setupId)
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

    public virtual async Task<SetupImportResult> ImportSetupAsync(CancellationToken cancellationToken = default)
    {
        logger.Information("Starting setup import");

        var file = await filesService.OpenSetupFileAsync();
        if (file is null)
        {
            logger.Information("Setup import canceled");
            return new SetupImportResult.Canceled();
        }

        SetupImportPayload? payload;
        try
        {
            payload = await backgroundTaskRunner.RunAsync(async () =>
            {
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync(cancellationToken);
                return Setup.FromJson(json);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup import failed while reading file");
            return new SetupImportResult.Failed(e.Message);
        }

        if (payload is null)
        {
            logger.Warning("Setup import rejected because the selected file was not a valid setup file");
            return new SetupImportResult.InvalidFile("JSON file was not a valid setup file.");
        }

        try
        {
            await databaseService.PutAsync(payload.Bike);
            var bikeSnapshot = BikeSnapshot.From(payload.Bike);
            bikeStore.Upsert(bikeSnapshot);

            var (resolvedBoardId, boardWarning) = ResolveImportedBoardId(payload.BoardId);

            await databaseService.PutAsync(payload.Setup);
            await ReassignBoardAsync(originalBoardId: null, resolvedBoardId, payload.Setup.Id);
            var setupSnapshot = SetupSnapshot.From(payload.Setup, resolvedBoardId);
            setupStore.Upsert(setupSnapshot);

            logger.Information(
                "Setup import completed for {SetupId} (bike {BikeId}, board {BoardId})",
                payload.Setup.Id,
                payload.Bike.Id,
                resolvedBoardId);

            return new SetupImportResult.Imported(
                new ImportedSetupEditorData(setupSnapshot, bikeSnapshot, boardWarning));
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup import failed during persistence");
            return new SetupImportResult.Failed(e.Message);
        }
    }

    public virtual async Task<SetupExportResult> ExportSetupAsync(
        Setup setup,
        Bike bike,
        Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting setup export for {SetupId}", setup.Id);

        var file = await filesService.SaveSetupFileAsync(setup.Name);
        if (file is null)
        {
            logger.Information("Setup export canceled for {SetupId}", setup.Id);
            return new SetupExportResult.Canceled();
        }

        try
        {
            return await backgroundTaskRunner.RunAsync(async () =>
            {
                var json = setup.ToJson(bike, boardId);
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(json.AsMemory(), cancellationToken);

                logger.Information("Setup export completed for {SetupId}", setup.Id);
                return (SetupExportResult)new SetupExportResult.Exported();
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup export failed for {SetupId}", setup.Id);
            return new SetupExportResult.Failed(e.Message);
        }
    }

    private (Guid? ResolvedBoardId, string? Warning) ResolveImportedBoardId(Guid? importedBoardId)
    {
        if (!importedBoardId.HasValue)
        {
            return (null, null);
        }

        if (setupStore.FindByBoardId(importedBoardId.Value) is null)
        {
            return (importedBoardId, null);
        }

        return (null, $"Board ID {importedBoardId.Value} was dropped because another setup uses it.");
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

public abstract record SetupSaveResult
{
    private SetupSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated) : SetupSaveResult;
    public sealed record Conflict(SetupSnapshot CurrentSnapshot) : SetupSaveResult;
    public sealed record Failed(string ErrorMessage) : SetupSaveResult;
}

public sealed record SetupDeleteResult(SetupDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SetupDeleteOutcome
{
    Deleted,
    Failed
}
