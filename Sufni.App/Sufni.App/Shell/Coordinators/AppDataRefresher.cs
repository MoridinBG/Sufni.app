using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Video.Services;
using Sufni.App.Video.Stores;

namespace Sufni.App.Shell.Coordinators;

public interface IAppDataRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

internal sealed class AppDataRefresher(
    IBikeStoreWriter bikeStore,
    ISetupStoreWriter setupStore,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IPairedDeviceStoreWriter pairedDeviceStore,
    ISessionVideoStoreWriter sessionVideoStore,
    ISessionSegmentStoreWriter sessionSegmentStore,
    ISynchronizableRepository<Bike> bikeRepository,
    ISynchronizableRepository<Setup> setupRepository,
    ISynchronizableRepository<Board> boardRepository,
    ISessionRepository sessionRepository,
    IRecordedSessionSourceRepository sourceRepository,
    IPairedDeviceRepository pairedDeviceRepository,
    ISessionVideoRepository videoRepository,
    ISessionSegmentRepository segmentRepository,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher dispatcher,
    StorePublication publication,
    ITrackStoreWriter trackStore,
    ITrackRepository trackRepository,
    IBoardStoreWriter boardStore) : IAppDataRefresher
{
    private readonly Lock refreshGate = new();
    private Task? activeRefresh;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (refreshGate)
        {
            if (activeRefresh is { IsCompleted: false })
                return activeRefresh.WaitAsync(cancellationToken);

            activeRefresh = RefreshCoreAsync(cancellationToken);
            return activeRefresh;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await backgroundTaskRunner.RunAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bikes = await bikeRepository.GetAllAsync();
            var setups = await setupRepository.GetAllAsync();
            var boards = await boardRepository.GetAllAsync();
            var sessions = await sessionRepository.GetSessionsAsync();
            var sources = await sourceRepository.GetRecordedSessionSourceSnapshotsAsync();
            var devices = await pairedDeviceRepository.GetPairedDevicesAsync();
            var clips = await videoRepository.GetLiveAsync();
            var tracks = await trackRepository.GetTrackSnapshotsAsync();
            var segments = await segmentRepository.GetLiveAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return new AppDataSnapshot(
                bikes.Select(BikeSnapshot.From).ToArray(),
                setups.Select(setup => SetupSnapshot.From(setup, boards.FirstOrDefault(board => board.SetupId == setup.Id)?.Id)).ToArray(),
                sessions.Select(SessionSnapshot.From).ToArray(),
                sources.ToArray(),
                devices.Select(PairedDeviceSnapshot.From).ToArray(),
                clips.Select(SessionVideoSnapshot.From).ToArray(),
                segments.Select(SessionSegmentSnapshot.From).ToArray(),
                tracks, boards.Select(BoardSnapshot.From).ToArray());
        }, cancellationToken);

        await dispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // These replacements complete synchronously on the shared dispatcher.
            using var batch = publication.BeginBatch();
            await bikeStore.ReplaceAllAsync(snapshot.Bikes);
            await setupStore.ReplaceAllAsync(snapshot.Setups);
            await sessionStore.ReplaceAllAsync(snapshot.Sessions);
            await recordedSessionSourceStore.ReplaceAllAsync(snapshot.Sources);
            await pairedDeviceStore.ReplaceAllAsync(snapshot.Devices);
            await sessionVideoStore.ReplaceAllAsync(snapshot.Videos);
            await sessionSegmentStore.ReplaceAllAsync(snapshot.Segments);
            await trackStore.ReplaceAllAsync(snapshot.Tracks);
            await boardStore.ReplaceAllAsync(snapshot.Boards);
        });
    }

    private sealed record AppDataSnapshot(
        IReadOnlyList<BikeSnapshot> Bikes,
        IReadOnlyList<SetupSnapshot> Setups,
        IReadOnlyList<SessionSnapshot> Sessions,
        IReadOnlyList<RecordedSessionSourceSnapshot> Sources,
        IReadOnlyList<PairedDeviceSnapshot> Devices,
        IReadOnlyList<SessionVideoSnapshot> Videos,
        IReadOnlyList<SessionSegmentSnapshot> Segments,
        IReadOnlyList<TrackSnapshot> Tracks,
        IReadOnlyList<BoardSnapshot> Boards);
}
