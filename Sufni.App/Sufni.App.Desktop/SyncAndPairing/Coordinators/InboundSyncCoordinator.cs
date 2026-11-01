using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
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
/// Desktop-only singleton owning the bike, setup, board, custom-layer, and segment branch of the
/// synchronization server's <c>SynchronizationDataArrived</c> event.
/// Session generations/sources are owned by the session sync applier; paired
/// devices by <see cref="PairedDeviceCoordinator"/>. Each entity
/// family has exactly one inbound owner.
/// </summary>
public sealed class InboundSyncCoordinator : IInboundSyncCoordinator
{
    private static readonly ILogger logger = Log.ForContext<InboundSyncCoordinator>();

    private readonly IBoardStoreWriter boardStore;
    private readonly ISynchronizableRepository<CustomTileLayer> customLayerRepository;
    private readonly ICustomTileLayerStoreWriter customLayerStore;
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
        StorePublication publication,
        ISynchronizableRepository<CustomTileLayer> customLayerRepository, ICustomTileLayerStoreWriter customLayerStore)
    {
        this.bikeRepository = bikeRepository;
        this.customLayerRepository = customLayerRepository;
        this.customLayerStore = customLayerStore;
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
                foreach (var bikeId in e.Data.Bikes.Select(row => row.Id).Concat(e.Data.Deletions.Where(row => row.EntityTable == "bike").Select(row => row.Id)).Distinct())
                {
                    var freshBike = await backgroundTaskRunner.RunAsync(async () =>
                    {
                        var row = await bikeRepository.GetAsync(bikeId);
                        return row is null ? null : BikeSnapshot.From(row);
                    });
                    if (freshBike is null)
                    {
                        removedBikeIds.Add(bikeId);
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
                var boards = e.Data.Boards.Count == 0 && !e.Data.Deletions.Any(row => row.EntityTable == "board") ? [] : await backgroundTaskRunner.RunAsync(boardRepository.GetAllAsync);
                foreach (var setupId in e.Data.Setups.Select(row => row.Id).Concat(e.Data.Deletions.Where(row => row.EntityTable == "setup").Select(row => row.Id)).Distinct())
                {
                    var freshSetup = await backgroundTaskRunner.RunAsync(async () =>
                    {
                        var row = await setupRepository.GetAsync(setupId);
                        return row is null ? null : SetupSnapshot.From(row);
                    });
                    if (freshSetup is null)
                    {
                        removedSetupIds.Add(setupId);
                        removedSetupCount++;
                    }
                    else
                    {
                        changedSetups.Add(freshSetup);
                        upsertedSetupCount++;
                    }
                }

                var changedBoardIds = e.Data.Boards.Select(board => board.Id).Concat(e.Data.Deletions.Where(row => row.EntityTable == "board").Select(row => row.Id)).ToHashSet();
                var boardSnapshots = boards.Where(board => changedBoardIds.Contains(board.Id)).Select(BoardSnapshot.From).ToArray();
                var removedBoardIds = changedBoardIds.Except(boardSnapshots.Select(board => board.Id)).ToArray();
                var segments = e.Data.Segments.Count == 0 && !e.Data.Deletions.Any(row => row.EntityTable == "session_segment") ? null : await backgroundTaskRunner.RunAsync(async () =>
                    (await segmentRepository.GetLiveAsync()).Select(SessionSegmentSnapshot.From).ToArray());

                var layerIds = e.Data.CustomTileLayers.Select(row => row.Id)
                    .Concat(e.Data.Deletions.Where(row => row.EntityTable == "custom_tile_layer").Select(row => row.Id)).ToHashSet();
                var layers = layerIds.Count == 0 ? [] : await backgroundTaskRunner.RunAsync(async () =>
                    (await customLayerRepository.GetAllAsync()).Where(row => layerIds.Contains(row.Id)).Select(CustomTileLayerSnapshot.From).ToArray());
                using var batch = publication.BeginBatch();
                await customLayerStore.PublishChangedAsync(layers);
                await customLayerStore.PublishRemovedAsync(layerIds.Except(layers.Select(row => row.Id)).ToArray());
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
