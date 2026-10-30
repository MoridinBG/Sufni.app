using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Models;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Setups.Coordinators;

public class SetupCoordinator(
    ISetupStoreWriter setupStore,
    IBikeStoreWriter bikeStore,
    IFilesService filesService,
    IBackgroundTaskRunner backgroundTaskRunner,
    ISetupPersistenceTransactionRunner setupPersistenceTransactions)
    : ISetupCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SetupCoordinator>();

    public SetupSnapshot CreateDraft(Guid? suggestedBoardId = null)
    {
        // Honour the suggested board ID only if no other setup claims it.
        Guid? actualBoardId = null;
        if (suggestedBoardId.HasValue && setupStore.FindByBoardId(suggestedBoardId.Value) is null)
        {
            actualBoardId = suggestedBoardId;
        }

        var seed = new Setup(Guid.NewGuid(), "new setup");
        var snapshot = SetupSnapshot.From(seed, actualBoardId);
        return snapshot;
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
            if (current?.BoardId != boardId)
            {
                logger.Verbose(
                    "Reassigning setup board association for {SetupId} from {OriginalBoardId} to {NewBoardId}",
                    setup.Id,
                    current?.BoardId,
                    boardId);
            }

            await backgroundTaskRunner.RunAsync(() => setupPersistenceTransactions.SaveSetupAsync(setup, current?.BoardId, boardId));

            var saved = SetupSnapshot.From(setup, boardId);
            await setupStore.PublishSetupsChangedAsync([saved]);
            logger.Information("Setup save completed for {SetupId}", setup.Id);
            return new SetupSaveResult.Saved(saved.Id, saved.Updated);
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup save failed for {SetupId}", setup.Id);
            return new SetupSaveResult.Failed(SetupSaveFailureReason.Save, e.Message);
        }
    }

    public async Task<SetupDeleteResult> DeleteAsync(Guid setupId)
    {
        logger.Information("Starting setup delete for {SetupId}", setupId);

        var snapshot = setupStore.Get(setupId);

        try
        {
            await backgroundTaskRunner.RunAsync(() => setupPersistenceTransactions.DeleteSetupAsync(setupId, snapshot?.BoardId));
            await setupStore.PublishSetupsRemovedAsync([setupId]);
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup delete failed for {SetupId}", setupId);
            return new SetupDeleteResult.Failed(SetupDeleteFailureReason.Delete, e.Message);
        }

        logger.Information("Setup delete completed for {SetupId}", setupId);
        return new SetupDeleteResult.Deleted();
    }

    public async Task<SetupImportResult> ImportSetupAsync(CancellationToken cancellationToken = default)
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
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or Sufni.Kinematics.LeverageRatioValidationException)
        {
            logger.Warning(e, "Setup import rejected because the selected file was invalid");
            return new SetupImportResult.InvalidFile("JSON file was not a valid setup file.");
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup import failed while reading file");
            return new SetupImportResult.Failed(SetupImportFailureReason.Import, e.Message);
        }

        if (payload is null)
        {
            logger.Warning("Setup import rejected because the selected file was not a valid setup file");
            return new SetupImportResult.InvalidFile("JSON file was not a valid setup file.");
        }

        try
        {
            var (resolvedBoardId, boardWarning) = ResolveImportedBoardId(payload.BoardId);

            await backgroundTaskRunner.RunAsync(() => setupPersistenceTransactions.ImportSetupAsync(
                payload.Bike, payload.Setup, resolvedBoardId, cancellationToken), cancellationToken);

            var bikeSnapshot = BikeSnapshot.From(payload.Bike);
            var setupSnapshot = SetupSnapshot.From(payload.Setup, resolvedBoardId);
            await bikeStore.PublishBikesChangedAsync([bikeSnapshot]);
            await setupStore.PublishSetupsChangedAsync([setupSnapshot]);

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
            return new SetupImportResult.Failed(SetupImportFailureReason.Import, e.Message);
        }
    }

    public async Task<SetupExportResult> ExportSetupAsync(
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
            return new SetupExportResult.Failed(SetupExportFailureReason.Export, e.Message);
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

}

public abstract record SetupSaveResult
{
    private SetupSaveResult() { }

    public sealed record Saved(Guid Id, long NewBaselineUpdated) : SetupSaveResult;
    public sealed record Conflict(SetupSnapshot CurrentSnapshot) : SetupSaveResult;
    public sealed record Failed(SetupSaveFailureReason Reason, string? Details = null) : SetupSaveResult;
}

public abstract record SetupDeleteResult
{
    private SetupDeleteResult() { }
    public sealed record Deleted : SetupDeleteResult;
    public sealed record Failed(SetupDeleteFailureReason Reason, string? Details = null) : SetupDeleteResult;
}
