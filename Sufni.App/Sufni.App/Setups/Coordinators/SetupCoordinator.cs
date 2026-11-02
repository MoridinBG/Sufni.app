using Sufni.App.Bikes.Coordinators;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Services;
using Sufni.App.Setups.Queries;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Shared.Stores;
using System;
using System.Text.Json;
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

internal class SetupCoordinator(
    ISetupStoreWriter setupStore,
    IBikeStoreWriter bikeStore,
    ISetupFileService filesService,
    IBikeSaveValidator bikeValidator,
    IBackgroundTaskRunner backgroundTaskRunner,
    ISetupPersistenceTransactionRunner setupPersistenceTransactions,
    ISynchronizableRepository<Board> boardRepository,
    IBoardStoreWriter boardStore,
    IUiThreadDispatcher dispatcher,
    StorePublication publication,
    ISetupBoardQuery setupQuery,
    ISynchronizableRepository<Bike> bikeRepository)
    : ISetupCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SetupCoordinator>();

    public SetupDetails CreateDraft(Guid? suggestedBoardId = null)
    {
        var seed = new Setup(Guid.NewGuid(), "new setup");
        var snapshot = new SetupDetails(SetupSnapshot.From(seed), suggestedBoardId);
        return snapshot;
    }

    public async Task<SetupSaveResult> SaveAsync(SetupDraft draft, SetupDraft? draftBase)
    {
        logger.Information("Starting setup save for {SetupId}", draft.Id);
        if (SetupSaveRules.Validate(draft, bikeStore.Get(draft.BikeId)) is { } invalid)
        {
            logger.Warning("Setup save rejected for {SetupId}: {Reason}", draft.Id, invalid);
            return new SetupSaveResult.Invalid(invalid);
        }

        try
        {
            SetupDetails? current;
            SetupDraft? currentDraft;
            do
            {
                current = setupQuery.Get(draft.Id);
                currentDraft = await backgroundTaskRunner.RunAsync(() => current is null ? null : SetupDraft.From(current.Setup, current.BoardId));
            } while (current != setupQuery.Get(draft.Id));
            if (draftBase is not null && current is null) return new SetupSaveResult.Missing();
            if (current is not null && (draftBase is null || currentDraft != draftBase))
                return new SetupSaveResult.Conflict(current.Setup, current.BoardId);

            var prepared = await backgroundTaskRunner.RunAsync(async () =>
            {
                var row = BuildRow(draft);
                var result = await setupPersistenceTransactions.SaveSetupAsync(row, current?.BoardId, draft.BoardId);
                (BoardSnapshot[] Changed, Guid[] Removed) boards = result is SetupWriteResult.Applied applied
                    ? await LoadBoardsAsync(applied.ChangedBoardIds) : ([], []);
                return (Result: result, Snapshot: SetupSnapshot.From(row), Boards: boards);
            });
            if (prepared.Result is SetupWriteResult.BoardInUse rejected)
            {
                logger.Warning("Setup save rejected for {SetupId}: board {BoardId} is already assigned", draft.Id, rejected.BoardId);
                return new SetupSaveResult.BoardInUse(rejected.BoardId);
            }
            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await setupStore.PublishSetupsChangedAsync([prepared.Snapshot]);
                await PublishBoardsAsync(prepared.Boards);
            });
            return new SetupSaveResult.Saved(prepared.Snapshot, draft.BoardId);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Setup save failed for {SetupId}", draft.Id);
            return new SetupSaveResult.Failed(SetupSaveFailureReason.Save, exception.Message);
        }
    }

    public async Task<SetupDeleteResult> DeleteAsync(Guid setupId)
    {
        logger.Information("Starting setup delete for {SetupId}", setupId);

        var snapshot = setupQuery.Get(setupId);

        try
        {
            var prepared = await backgroundTaskRunner.RunAsync(async () =>
            {
                var result = await setupPersistenceTransactions.DeleteSetupAsync(setupId, snapshot?.BoardId);
                (BoardSnapshot[] Changed, Guid[] Removed) boards = result is SetupDeletePersistenceResult.Deleted deleted
                    ? await LoadBoardsAsync(deleted.ChangedBoardIds) : ([], []);
                return (Result: result, Boards: boards);
            });
            if (prepared.Result is SetupDeletePersistenceResult.InUse)
            {
                logger.Warning("Setup delete rejected for {SetupId}: sessions still reference it", setupId);
                return new SetupDeleteResult.InUse();
            }
            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await setupStore.PublishSetupsRemovedAsync([setupId]);
                await PublishBoardsAsync(prepared.Boards);
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
            var imported = await backgroundTaskRunner.RunAsync(() =>
            {
                var setup = SetupDraft.From(SetupSnapshot.From(payload.Setup), payload.BoardId);
                var bike = BikeSnapshot.From(payload.Bike);
                return (Setup: setup, Bike: BikeDraft.From(payload.Bike), Invalid: SetupSaveRules.Validate(setup, bike));
            });
            if (imported.Invalid is { } invalid)
            {
                logger.Warning("Setup import rejected for {SetupId}: {Reason}", imported.Setup.Id, invalid);
                return new SetupImportResult.InvalidFile(FailurePresentation.From(invalid).Summary);
            }
            switch (await bikeValidator.ValidateAsync(imported.Bike))
            {
                case BikeSaveValidation.Invalid invalidBike:
                    return new SetupImportResult.InvalidFile(invalidBike.Message);
                case BikeSaveValidation.Failed failed:
                    return new SetupImportResult.Failed(SetupImportFailureReason.Import, failed.Details);
            }

            var prepared = await backgroundTaskRunner.RunAsync(async () =>
            {
                var result = await setupPersistenceTransactions.ImportSetupAsync(payload.Bike, payload.Setup, payload.BoardId);
                (BoardSnapshot[] Changed, Guid[] Removed) boards = result is SetupWriteResult.Applied applied
                    ? await LoadBoardsAsync(applied.ChangedBoardIds) : ([], []);
                return (Result: result, Boards: boards, Bike: BikeSnapshot.From(payload.Bike), Setup: SetupSnapshot.From(payload.Setup));
            });
            if (prepared.Result is SetupWriteResult.BoardInUse rejected)
            {
                logger.Warning("Setup import rejected for {SetupId}: board {BoardId} is already assigned", payload.Setup.Id, rejected.BoardId);
                return new SetupImportResult.BoardInUse(rejected.BoardId);
            }
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
                payload.BoardId);

            return new SetupImportResult.Imported(prepared.Setup.Id);
        }
        catch (JsonException exception)
        {
            logger.Warning(exception, "Setup import rejected because sensor calibration was invalid");
            return new SetupImportResult.InvalidFile("The setup file contains an invalid sensor configuration.");
        }
        catch (Exception e)
        {
            logger.Error(e, "Setup import failed during persistence");
            return new SetupImportResult.Failed(SetupImportFailureReason.Import, e.Message);
        }
    }

    public async Task<SetupExportResult> ExportSetupAsync(SetupDraft draft, CancellationToken cancellationToken = default)
    {
        var bike = bikeStore.Get(draft.BikeId);
        if (bike is null) return new SetupExportResult.Failed(SetupExportFailureReason.BikeUnavailable);
        try
        {
            var payload = await backgroundTaskRunner.RunAsync(async () =>
                (Setup: BuildRow(draft), Bike: await bikeRepository.GetAsync(draft.BikeId)), cancellationToken);
            if (payload.Bike is null) return new SetupExportResult.Failed(SetupExportFailureReason.BikeUnavailable);
            return await filesService.ExportAsync(payload.Setup, payload.Bike, draft.BoardId, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Setup export preparation failed for {SetupId}", draft.Id);
            return new SetupExportResult.Failed(SetupExportFailureReason.Export, exception.Message);
        }
    }

    private static Setup BuildRow(SetupDraft draft) => new(draft.Id, draft.Name!)
    {
        BikeId = draft.BikeId,
        FrontSensorConfigurationJson = SensorJson(draft.FrontSensor),
        RearSensorConfigurationJson = SensorJson(draft.RearSensor),
    };

    private static string? SensorJson(SensorDraft? draft) => draft is null ? null : SensorConfiguration.ToJson(draft switch
    {
        SensorDraft.LinearFork value => new LinearForkSensorConfiguration { Length = value.Length!.Value, Resolution = value.Resolution!.Value },
        SensorDraft.LinearShock value => new LinearShockSensorConfiguration { Length = value.Length!.Value, Resolution = value.Resolution!.Value, Type = value.Type },
        SensorDraft.RotationalFork value => new RotationalForkSensorConfiguration { MaxLength = value.MaxLength!.Value, ArmLength = value.ArmLength!.Value },
        SensorDraft.RotationalShock value => new RotationalShockSensorConfiguration
        {
            CentralJoint = value.CentralJoint!, AdjacentJoint1 = value.AdjacentJoint1!, AdjacentJoint2 = value.AdjacentJoint2!,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(draft)),
    });

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


}

public abstract record SetupSaveResult
{
    private SetupSaveResult() { }

    public sealed record Saved(SetupSnapshot Snapshot, Guid? BoardId) : SetupSaveResult;
    public sealed record Missing : SetupSaveResult;
    public sealed record BoardInUse(Guid BoardId) : SetupSaveResult;
    public sealed record Invalid(SetupValidationFailureReason Reason) : SetupSaveResult;
    public sealed record Conflict(SetupSnapshot CurrentSnapshot, Guid? BoardId = null) : SetupSaveResult;
    public sealed record Failed(SetupSaveFailureReason Reason, string? Details = null) : SetupSaveResult;
}

public abstract record SetupDeleteResult
{
    private SetupDeleteResult() { }
    public sealed record Deleted : SetupDeleteResult;
    public sealed record InUse : SetupDeleteResult;
    public sealed record Failed(SetupDeleteFailureReason Reason, string? Details = null) : SetupDeleteResult;
}
