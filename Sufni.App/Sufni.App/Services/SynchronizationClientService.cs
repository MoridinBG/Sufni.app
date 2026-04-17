using System.Threading.Tasks;
using Sufni.App.Models;
using Serilog;

namespace Sufni.App.Services;

public class SynchronizationClientService : ISynchronizationClientService
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationClientService>();

    private readonly IDatabaseService databaseService;
    private readonly IHttpApiService httpApiService;

    public SynchronizationClientService(IDatabaseService databaseService, IHttpApiService httpApiService)
    {
        this.databaseService = databaseService;
        this.httpApiService = httpApiService;
    }

    private async Task PushLocalChanges(long lastSyncTime)
    {
        var changes = new SynchronizationData
        {
            Boards = await databaseService.GetChangedAsync<Board>(lastSyncTime),
            Bikes = await databaseService.GetChangedAsync<Bike>(lastSyncTime),
            Setups = await databaseService.GetChangedAsync<Setup>(lastSyncTime),
            Sessions = await databaseService.GetChangedAsync<Session>(lastSyncTime),
            Tracks = await databaseService.GetChangedAsync<Track>(lastSyncTime)
        };

        logger.Verbose(
            "Pushing local changes since {LastSyncTime} with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, and {TrackCount} tracks",
            lastSyncTime,
            changes.Boards.Count,
            changes.Bikes.Count,
            changes.Setups.Count,
            changes.Sessions.Count,
            changes.Tracks.Count);

        await httpApiService.PushSyncAsync(changes);
    }

    private async Task PushIncompleteSessions()
    {
        var incompleteSessions = await httpApiService.GetIncompleteSessionIdsAsync();
        var uploadedCount = 0;

        foreach (var id in incompleteSessions)
        {
            var psst = await databaseService.GetSessionRawPsstAsync(id);
            if (psst is not null)
            {
                await httpApiService.PatchSessionPsstAsync(id, psst);
                uploadedCount++;
            }
        }

        logger.Verbose(
            "Pushed {UploadedCount} incomplete sessions out of {IncompleteSessionCount} requested by the server",
            uploadedCount,
            incompleteSessions.Count);
    }

    private async Task PullRemoteChanges(long lastSyncTime)
    {
        var syncData = await httpApiService.PullSyncAsync(lastSyncTime);
        var removedBoardCount = 0;
        var upsertedBoardCount = 0;
        foreach (var board in syncData.Boards)
        {
            if (board.Deleted.HasValue)
            {
                await databaseService.DeleteAsync(board);
                removedBoardCount++;
            }
            else
            {
                await databaseService.PutAsync(board);
                upsertedBoardCount++;
            }
        }

        var removedBikeCount = 0;
        var upsertedBikeCount = 0;
        foreach (var bike in syncData.Bikes)
        {
            if (bike.Deleted.HasValue)
            {
                await databaseService.DeleteAsync(bike);
                removedBikeCount++;
            }
            else
            {
                await databaseService.PutAsync(bike);
                upsertedBikeCount++;
            }
        }

        var removedSetupCount = 0;
        var upsertedSetupCount = 0;
        foreach (var setup in syncData.Setups)
        {
            if (setup.Deleted.HasValue)
            {
                await databaseService.DeleteAsync(setup);
                removedSetupCount++;
            }
            else
            {
                await databaseService.PutAsync(setup);
                upsertedSetupCount++;
            }
        }

        var removedTrackCount = 0;
        var upsertedTrackCount = 0;
        foreach (var track in syncData.Tracks)
        {
            if (track.Deleted.HasValue)
            {
                await databaseService.DeleteAsync(track);
                removedTrackCount++;
            }
            else
            {
                await databaseService.PutAsync(track);
                upsertedTrackCount++;
            }
        }

        var removedSessionCount = 0;
        var upsertedSessionCount = 0;
        foreach (var session in syncData.Sessions)
        {
            if (session.Deleted.HasValue)
            {
                await databaseService.DeleteAsync(session);
                removedSessionCount++;
            }
            else
            {
                await databaseService.PutSessionAsync(session);
                upsertedSessionCount++;
            }
        }

        logger.Verbose(
            "Pulled remote changes with {RemovedBoardCount}/{UpsertedBoardCount} boards, {RemovedBikeCount}/{UpsertedBikeCount} bikes, {RemovedSetupCount}/{UpsertedSetupCount} setups, {RemovedTrackCount}/{UpsertedTrackCount} tracks, and {RemovedSessionCount}/{UpsertedSessionCount} sessions removed/upserted",
            removedBoardCount,
            upsertedBoardCount,
            removedBikeCount,
            upsertedBikeCount,
            removedSetupCount,
            upsertedSetupCount,
            removedTrackCount,
            upsertedTrackCount,
            removedSessionCount,
            upsertedSessionCount);
    }

    private async Task PullIncompleteSessions()
    {
        var incompleteSessionIds = await databaseService.GetIncompleteSessionIdsAsync();
        var downloadedCount = 0;

        foreach (var id in incompleteSessionIds)
        {
            var psst = await httpApiService.GetSessionPsstAsync(id);
            if (psst is not null)
            {
                await databaseService.PatchSessionPsstAsync(id, psst);
                downloadedCount++;
            }
        }

        logger.Verbose(
            "Pulled {DownloadedCount} incomplete sessions out of {IncompleteSessionCount} local placeholders",
            downloadedCount,
            incompleteSessionIds.Count);
    }

    public async Task SyncAll()
    {
        try
        {
            var lastSyncTime = await databaseService.GetLastSyncTimeAsync(httpApiService.ServerUrl);

            logger.Verbose("Starting synchronization client run with last sync time {LastSyncTime}", lastSyncTime);

            await PushLocalChanges(lastSyncTime);
            await PullRemoteChanges(lastSyncTime);
            await PushIncompleteSessions();
            await PullIncompleteSessions();

            await databaseService.UpdateLastSyncTimeAsync(httpApiService.ServerUrl);
            logger.Verbose("Synchronization client run completed");
        }
        catch (System.Exception exception)
        {
            logger.Error(exception, "Synchronization client run failed");
            throw;
        }
    }
}
