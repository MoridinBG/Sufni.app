using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;

namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Merges replicated rows and publishes their committed winners together.</summary>
public sealed class SynchronizationApplier(
    ISyncDataStore syncDataStore,
    ISynchronizableRepository<Bike> bikeRepository,
    ISynchronizableRepository<Setup> setupRepository,
    ISynchronizableRepository<Board> boardRepository,
    ISynchronizableRepository<CustomTileLayer> customLayerRepository,
    ISessionRepository sessionRepository,
    ITrackRepository trackRepository,
    ISessionSegmentRepository segmentRepository,
    IBikeStoreWriter bikeStore,
    ISetupStoreWriter setupStore,
    IBoardStoreWriter boardStore,
    ICustomTileLayerStoreWriter customLayerStore,
    ISessionStoreWriter sessionStore,
    ITrackStoreWriter trackStore,
    ISessionSegmentStoreWriter segmentStore,
    StorePublication publication,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher uiThreadDispatcher) : ISynchronizationApplier
{
    public Task<SynchronizationApplyResult> ApplyAsync(SynchronizationData data, SynchronizationDirection direction) =>
        backgroundTaskRunner.RunAsync(async () =>
        {
            var result = direction == SynchronizationDirection.Push
                ? await syncDataStore.MergeAllAsync(data)
                : await syncDataStore.ApplyRemoteSynchronizationDataAsync(data);
            var bikes = await ReadAsync(result.BikeIds, async id =>
                await bikeRepository.GetAsync(id) is { } row ? BikeSnapshot.From(row) : null);
            var setups = await ReadAsync(result.SetupIds, async id =>
                await setupRepository.GetAsync(id) is { } row ? SetupSnapshot.From(row) : null);
            var boards = await ReadAsync(result.BoardIds, async id =>
                await boardRepository.GetAsync(id) is { } row ? BoardSnapshot.From(row) : null);
            var layers = await ReadAsync(result.CustomLayerIds, async id =>
                await customLayerRepository.GetAsync(id) is { } row ? CustomTileLayerSnapshot.From(row) : null);
            var sessions = await ReadAsync(result.SessionIds, async id =>
                await sessionRepository.GetSessionAsync(id) is { } row ? SessionSnapshot.From(row) : null);
            var tracks = await trackRepository.GetTrackSnapshotsAsync(result.TrackIds);
            var removedTracks = result.TrackIds.Except(tracks.Select(row => row.Id)).ToArray();
            var segments = result.SegmentIds.Count == 0 ? [] : (await segmentRepository.GetLiveAsync())
                .Where(row => result.SegmentIds.Contains(row.Id)).Select(SessionSegmentSnapshot.From).ToArray();
            var removedSegments = result.SegmentIds.Except(segments.Select(row => row.Id)).ToArray();

            await uiThreadDispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await bikeStore.PublishBikesRemovedAsync(bikes.Removed);
                await bikeStore.PublishBikesChangedAsync(bikes.Snapshots);
                await setupStore.PublishSetupsRemovedAsync(setups.Removed);
                await setupStore.PublishSetupsChangedAsync(setups.Snapshots);
                await boardStore.PublishBoardsRemovedAsync(boards.Removed);
                await boardStore.PublishBoardsChangedAsync(boards.Snapshots);
                await customLayerStore.PublishRemovedAsync(layers.Removed);
                await customLayerStore.PublishChangedAsync(layers.Snapshots);
                await sessionStore.PublishSessionsRemovedAsync(sessions.Removed);
                await sessionStore.PublishSessionsChangedAsync(sessions.Snapshots);
                await trackStore.PublishTracksRemovedAsync(removedTracks);
                await trackStore.PublishTracksChangedAsync(tracks);
                await segmentStore.PublishSegmentsAsync(segments, removedSegments);
            });
            return result;
        });

    private static async Task<(List<T> Snapshots, List<Guid> Removed)> ReadAsync<T>(
        IReadOnlyList<Guid> ids, Func<Guid, Task<T?>> read) where T : class
    {
        var snapshots = new List<T>();
        var removed = new List<Guid>();
        foreach (var id in ids)
        {
            if (await read(id) is { } snapshot) snapshots.Add(snapshot);
            else removed.Add(id);
        }
        return (snapshots, removed);
    }
}
