using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Shared.Stores;
using System.Linq;
using Sufni.App.Segments.Services;
using System;
using System.Collections.Generic;
using Serilog;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Segments.Stores;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Infrastructure;
namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>
/// Desktop-only singleton owning the bike, setup, board, and segment branch of the
/// synchronization server's <c>SynchronizationDataArrived</c> event.
/// Session generations/sources are owned by the session sync applier; paired
/// devices by <see cref="PairedDeviceCoordinator"/>. Each entity
/// family has exactly one inbound owner.
/// </summary>
public sealed class InboundSyncCoordinator : IInboundSyncCoordinator
{
    private static readonly ILogger logger = Log.ForContext<InboundSyncCoordinator>();

    private readonly IBoardStoreWriter boardStore;
    private readonly StorePublication publication;
    private readonly ISynchronizableRepository<Bike> bikeRepository;
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly ISynchronizableRepository<Board> boardRepository;
    private readonly IBikeStoreWriter bikeStoreWriter;
    private readonly ISetupStoreWriter setupStoreWriter;
    private readonly ISessionSegmentStoreWriter segmentStoreWriter;
    private readonly ISessionSegmentRepository segmentRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public InboundSyncCoordinator(
        ISynchronizableRepository<Bike> bikeRepository,
        ISynchronizableRepository<Setup> setupRepository,
        IBikeStoreWriter bikeStoreWriter,
        ISetupStoreWriter setupStoreWriter,
        ISessionSegmentStoreWriter segmentStoreWriter,
        ISynchronizationServerService synchronizationServer,
        IUiThreadDispatcher uiThreadDispatcher,
        ISessionSegmentRepository segmentRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISynchronizableRepository<Board> boardRepository,
        IBoardStoreWriter boardStore,
        StorePublication publication)
    {
        this.bikeRepository = bikeRepository;
        this.boardStore = boardStore;
        this.publication = publication;
        this.boardRepository = boardRepository;
        this.setupRepository = setupRepository;
        this.bikeStoreWriter = bikeStoreWriter;
        this.setupStoreWriter = setupStoreWriter;
        this.segmentStoreWriter = segmentStoreWriter;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.segmentRepository = segmentRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;

        synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
    }

    private void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        _ = uiThreadDispatcher.InvokeAsync(async () =>
        {
            try
            {
                var removedBikeIds = new List<Guid>();
                var changedBikes = new List<BikeSnapshot>();
                var removedBikeCount = 0;
                var upsertedBikeCount = 0;
                foreach (var bike in e.Data.Bikes)
                {
                    var freshBike = await backgroundTaskRunner.RunAsync(async () =>
                    {
                        var row = await bikeRepository.GetAsync(bike.Id);
                        return row is null ? null : BikeSnapshot.From(row);
                    });
                    if (freshBike is null)
                    {
                        removedBikeIds.Add(bike.Id);
                        removedBikeCount++;
                    }
                    else
                    {
                        changedBikes.Add(freshBike);
                        upsertedBikeCount++;
                    }
                }

                var removedSetupCount = 0;
                var upsertedSetupCount = 0;
                var removedSetupIds = new List<Guid>();
                var changedSetups = new List<SetupSnapshot>();
                var boards = e.Data.Boards.Count == 0 ? [] : await backgroundTaskRunner.RunAsync(boardRepository.GetAllAsync);
                foreach (var setup in e.Data.Setups)
                {
                    var freshSetup = await backgroundTaskRunner.RunAsync(async () =>
                    {
                        var row = await setupRepository.GetAsync(setup.Id);
                        return row is null ? null : SetupSnapshot.From(row);
                    });
                    if (freshSetup is null)
                    {
                        removedSetupIds.Add(setup.Id);
                        removedSetupCount++;
                    }
                    else
                    {
                        changedSetups.Add(freshSetup);
                        upsertedSetupCount++;
                    }
                }

                var changedBoardIds = e.Data.Boards.Select(board => board.Id).ToHashSet();
                var boardSnapshots = boards.Where(board => changedBoardIds.Contains(board.Id)).Select(BoardSnapshot.From).ToArray();
                var removedBoardIds = changedBoardIds.Except(boardSnapshots.Select(board => board.Id)).ToArray();
                var segments = e.Data.Segments.Count == 0 ? null : await backgroundTaskRunner.RunAsync(async () =>
                    (await segmentRepository.GetLiveAsync()).Select(SessionSegmentSnapshot.From).ToArray());

                using var batch = publication.BeginBatch();
                if (removedBikeIds.Count > 0)
                {
                    await bikeStoreWriter.PublishBikesRemovedAsync(removedBikeIds);
                }

                if (changedBikes.Count > 0)
                {
                    await bikeStoreWriter.PublishBikesChangedAsync(changedBikes);
                }

                await setupStoreWriter.PublishSetupsRemovedAsync(removedSetupIds);
                await setupStoreWriter.PublishSetupsChangedAsync(changedSetups);
                await boardStore.PublishBoardsRemovedAsync(removedBoardIds);
                await boardStore.PublishBoardsChangedAsync(boardSnapshots);
                if (segments is not null) await segmentStoreWriter.ReplaceAllAsync(segments);

                logger.Verbose(
                    "Applied inbound bike/setup synchronization with {RemovedBikeCount} bike removals, {UpsertedBikeCount} bike upserts, {RemovedSetupCount} setup removals, and {UpsertedSetupCount} setup upserts",
                    removedBikeCount,
                    upsertedBikeCount,
                    removedSetupCount,
                    upsertedSetupCount);
            }
            catch (System.Exception exception)
            {
                logger.Error(exception, "Failed to apply inbound bike/setup synchronization data");
            }
        });
    }
}
