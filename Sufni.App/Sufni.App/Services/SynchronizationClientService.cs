using System.Linq;
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
        var changes = await databaseService.GetSynchronizationDataAsync(lastSyncTime);

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
        await databaseService.ApplyRemoteSynchronizationDataAsync(syncData);

        logger.Verbose(
            "Pulled remote changes with {RemovedBoardCount}/{UpsertedBoardCount} boards, {RemovedBikeCount}/{UpsertedBikeCount} bikes, {RemovedSetupCount}/{UpsertedSetupCount} setups, {RemovedTrackCount}/{UpsertedTrackCount} tracks, and {RemovedSessionCount}/{UpsertedSessionCount} sessions removed/upserted",
            syncData.Boards.Count(board => board.Deleted.HasValue),
            syncData.Boards.Count(board => !board.Deleted.HasValue),
            syncData.Bikes.Count(bike => bike.Deleted.HasValue),
            syncData.Bikes.Count(bike => !bike.Deleted.HasValue),
            syncData.Setups.Count(setup => setup.Deleted.HasValue),
            syncData.Setups.Count(setup => !setup.Deleted.HasValue),
            syncData.Tracks.Count(track => track.Deleted.HasValue),
            syncData.Tracks.Count(track => !track.Deleted.HasValue),
            syncData.Sessions.Count(session => session.Deleted.HasValue),
            syncData.Sessions.Count(session => !session.Deleted.HasValue));
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

    private async Task PushIncompleteSessionSources()
    {
        var incompleteSourceIds = await httpApiService.GetIncompleteSessionSourceIdsAsync();
        var uploadedCount = 0;

        foreach (var id in incompleteSourceIds)
        {
            var source = await databaseService.GetRecordedSessionSourceAsync(id);
            if (source is not null)
            {
                if (!RecordedSessionSourceHash.Matches(source))
                {
                    continue;
                }

                await httpApiService.PatchRecordedSessionSourceAsync(ToTransfer(source));
                uploadedCount++;
            }
        }

        logger.Verbose(
            "Pushed {UploadedCount} incomplete recorded sources out of {IncompleteSourceCount} requested by the server",
            uploadedCount,
            incompleteSourceIds.Count);
    }

    private async Task PullIncompleteSessionSources()
    {
        var incompleteSourceIds = await databaseService.GetSessionIdsMissingRecordedSourceAsync();
        var downloadedCount = 0;

        foreach (var id in incompleteSourceIds)
        {
            var source = await httpApiService.GetRecordedSessionSourceAsync(id);
            if (source is not null)
            {
                if (!RecordedSessionSourceHash.Matches(source))
                {
                    continue;
                }

                await databaseService.PutRecordedSessionSourceAsync(FromTransfer(source));
                downloadedCount++;
            }
        }

        logger.Verbose(
            "Pulled {DownloadedCount} incomplete recorded sources out of {IncompleteSourceCount} local placeholders",
            downloadedCount,
            incompleteSourceIds.Count);
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
            await PushIncompleteSessionSources();
            await PullIncompleteSessionSources();

            await databaseService.UpdateLastSyncTimeAsync(httpApiService.ServerUrl);
            logger.Verbose("Synchronization client run completed");
        }
        catch (System.Exception exception)
        {
            logger.Error(exception, "Synchronization client run failed");
            throw;
        }
    }

    private static RecordedSessionSourceTransfer ToTransfer(RecordedSessionSource source) => new(
        source.SessionId,
        source.SourceKind,
        source.SourceName,
        source.SchemaVersion,
        source.SourceHash,
        source.Payload);

    private static RecordedSessionSource FromTransfer(RecordedSessionSourceTransfer source) => new()
    {
        SessionId = source.SessionId,
        SourceKind = source.SourceKind,
        SourceName = source.SourceName,
        SchemaVersion = source.SchemaVersion,
        SourceHash = source.SourceHash,
        Payload = source.Payload
    };
}
