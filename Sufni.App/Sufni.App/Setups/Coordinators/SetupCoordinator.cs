using Sufni.App.Setups.Services;
using Sufni.App.Setups.Queries;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Shared.Stores;
using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Models;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Setups.Coordinators;

public class SetupCoordinator(
    ISetupStoreWriter setupStore,
    IBikeStoreWriter bikeStore,
    ISetupFileService filesService,
    IBackgroundTaskRunner backgroundTaskRunner,
    ISetupPersistenceTransactionRunner setupPersistenceTransactions,
    ISynchronizableRepository<Board> boardRepository,
    IBoardStoreWriter boardStore,
    IUiThreadDispatcher dispatcher,
    StorePublication publication,
    ISetupBoardQuery setupQuery)
    : ISetupCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SetupCoordinator>();

    public SetupDetails CreateDraft(Guid? suggestedBoardId = null)
    {
        // Honour the suggested board ID only if no other setup claims it.
        Guid? actualBoardId = null;
        if (suggestedBoardId.HasValue && boardStore.Get(suggestedBoardId.Value)?.SetupId is null)
        {
            actualBoardId = suggestedBoardId;
        }

        var seed = new Setup(Guid.NewGuid(), "new setup");
        var snapshot = new SetupDetails(SetupSnapshot.From(seed), actualBoardId);
        return snapshot;
    }

    public async Task<SetupSaveResult> SaveAsync(Setup setup, Guid? boardId, long baselineUpdated)
    {
        logger.Information("Starting setup save for {SetupId}", setup.Id);

        var current = setupQuery.Get(setup.Id);
        if (current is not null && current.Setup.Updated > baselineUpdated)
        {
            logger.Warning("Setup save conflict for {SetupId}", setup.Id);
            return new SetupSaveResult.Conflict(current.Setup, current.BoardId);
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

            var boards = await backgroundTaskRunner.RunAsync(async () =>
                await LoadBoardsAsync(await setupPersistenceTransactions.SaveSetupAsync(setup, current?.BoardId, boardId)));

            var saved = SetupSnapshot.From(setup);
            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await setupStore.PublishSetupsChangedAsync([saved]);
                await PublishBoardsAsync(boards);
            });
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

        var snapshot = setupQuery.Get(setupId);

        try
        {
            var boards = await backgroundTaskRunner.RunAsync(async () =>
                await LoadBoardsAsync(await setupPersistenceTransactions.DeleteSetupAsync(setupId, snapshot?.BoardId)));
            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await setupStore.PublishSetupsRemovedAsync([setupId]);
                await PublishBoardsAsync(boards);
            });
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

        var read = await filesService.ReadAsync(cancellationToken);
        if (read is not SetupFileReadResult.Read loaded)
            return read switch
            {
                SetupFileReadResult.Canceled => new SetupImportResult.Canceled(),
                SetupFileReadResult.InvalidFile invalid => new SetupImportResult.InvalidFile(invalid.ErrorMessage),
                SetupFileReadResult.Failed failed => new SetupImportResult.Failed(failed.Reason, failed.Details),
                _ => throw new ArgumentOutOfRangeException(nameof(read)),
            };
        var payload = loaded.Payload;

        try
        {
            var (resolvedBoardId, boardWarning) = ResolveImportedBoardId(payload.BoardId);

            var prepared = await backgroundTaskRunner.RunAsync(async () =>
            {
                var boards = await LoadBoardsAsync(await setupPersistenceTransactions.ImportSetupAsync(
                    payload.Bike, payload.Setup, resolvedBoardId));
                return (Boards: boards, Bike: BikeSnapshot.From(payload.Bike), Setup: SetupSnapshot.From(payload.Setup));
            });
            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await bikeStore.PublishBikesChangedAsync([prepared.Bike]);
                await setupStore.PublishSetupsChangedAsync([prepared.Setup]);
                await PublishBoardsAsync(prepared.Boards);
            });

            logger.Information(
                "Setup import completed for {SetupId} (bike {BikeId}, board {BoardId})",
                payload.Setup.Id,
                payload.Bike.Id,
                resolvedBoardId);

            return new SetupImportResult.Imported(
                new ImportedSetupEditorData(prepared.Setup, prepared.Bike, boardWarning, resolvedBoardId));
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup import failed during persistence");
            return new SetupImportResult.Failed(SetupImportFailureReason.Import, e.Message);
        }
    }

    public Task<SetupExportResult> ExportSetupAsync(
        Setup setup, Bike bike, Guid? boardId, CancellationToken cancellationToken = default) =>
        filesService.ExportAsync(setup, bike, boardId, cancellationToken);

    private async Task<(BoardSnapshot[] Changed, Guid[] Removed)> LoadBoardsAsync(IReadOnlyList<Guid> ids)
    {
        var rows = await Task.WhenAll(ids.Select(boardRepository.GetAsync));
        var snapshots = rows.OfType<Board>().Select(BoardSnapshot.From).ToArray();
        return (snapshots, ids.Except(snapshots.Select(board => board.Id)).ToArray());
    }

    private async Task PublishBoardsAsync((BoardSnapshot[] Changed, Guid[] Removed) boards)
    {
        await boardStore.PublishBoardsChangedAsync(boards.Changed);
        await boardStore.PublishBoardsRemovedAsync(boards.Removed);
    }

    private (Guid? ResolvedBoardId, string? Warning) ResolveImportedBoardId(Guid? importedBoardId)
    {
        if (!importedBoardId.HasValue)
        {
            return (null, null);
        }

        if (boardStore.Get(importedBoardId.Value)?.SetupId is null)
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
    public sealed record Conflict(SetupSnapshot CurrentSnapshot, Guid? BoardId = null) : SetupSaveResult;
    public sealed record Failed(SetupSaveFailureReason Reason, string? Details = null) : SetupSaveResult;
}

public abstract record SetupDeleteResult
{
    private SetupDeleteResult() { }
    public sealed record Deleted : SetupDeleteResult;
    public sealed record Failed(SetupDeleteFailureReason Reason, string? Details = null) : SetupDeleteResult;
}
