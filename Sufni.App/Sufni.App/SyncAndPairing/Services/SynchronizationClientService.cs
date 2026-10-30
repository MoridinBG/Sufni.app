using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Processing.Services;
namespace Sufni.App.SyncAndPairing.Services;

public class SynchronizationClientService : ISynchronizationClientService
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationClientService>();
    public const string SyncStateKey = "paired-server";
    private const int SyncTransferDop = 3;
    private readonly ISyncDataStore syncDataStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery;
    private readonly IHttpApiService httpApiService;
    private readonly IAppPreferences appPreferences;
    private readonly TimeProvider timeProvider;

    public SynchronizationClientService(
        ISyncDataStore syncDataStore,
        ISessionRepository sessionRepository,
        ISessionStoreWriter sessionStore,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        IHttpApiService httpApiService,
        IAppPreferences appPreferences,
        TimeProvider timeProvider,
        ISessionTelemetryWriter sessionTelemetryWriter)
    {
        this.syncDataStore = syncDataStore;
        this.sessionRepository = sessionRepository;
        this.sessionStore = sessionStore;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionSourceSyncQuery = recordedSessionSourceSyncQuery;
        this.httpApiService = httpApiService;
        this.appPreferences = appPreferences;
        this.timeProvider = timeProvider;
    }

    private async Task PushLocalChanges(long sinceExclusive, CancellationToken cancellationToken)
    {
        var upperInclusive = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var changes = await syncDataStore.GetSynchronizationDataAsync(
            sinceExclusive,
            upperInclusive);
        changes.UpperBound = upperInclusive;
        changes.AppPreferences = await appPreferences.GetSyncDataAsync(
            sinceExclusive,
            upperInclusive);

        logger.Verbose(
            "Pushing local changes in ({SinceExclusive}, {UpperInclusive}] with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, {TrackCount} tracks, and app preferences present {HasAppPreferences}",
            sinceExclusive,
            upperInclusive,
            changes.Boards.Count,
            changes.Bikes.Count,
            changes.Setups.Count,
            changes.Sessions.Count,
            changes.Tracks.Count,
            changes.AppPreferences is not null);

        await httpApiService.PushSyncAsync(changes, cancellationToken: cancellationToken);
        await syncDataStore.UpdateLastPushTimeAsync(SyncStateKey, upperInclusive);
    }

    private async Task PushIncompleteSessions(CancellationToken cancellationToken)
    {
        var incompleteSessions = await httpApiService.GetIncompleteSessionIdsAsync(cancellationToken: cancellationToken);
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSessions, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
        {
            var blob = await sessionRepository.GetSessionRawPsstWithFingerprintAsync(id);
            if (blob is not null)
            {
                // Upload the bytes with their fingerprint so the hub rejects a
                // mismatch instead of storing bytes that contradict its metadata.
                await httpApiService.PatchSessionPsstAsync(id, blob.Value.Data, blob.Value.Fingerprint, cancellationToken: cancellationToken);
                Interlocked.Increment(ref uploadedCount);
            }
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete sessions out of {IncompleteSessionCount} requested by the server",
            uploadedCount,
            incompleteSessions.Count);
    }

    private async Task<(IReadOnlyList<SessionBlobSwap> Swaps, long UpperBound)> PullRemoteChanges(long sinceExclusive, CancellationToken cancellationToken)
    {
        var syncData = await httpApiService.PullSyncAsync(sinceExclusive, cancellationToken: cancellationToken);
        var swaps = await syncDataStore.ApplyRemoteSynchronizationDataAsync(syncData);
        await appPreferences.ApplySyncDataAsync(syncData.AppPreferences);
        logger.Verbose(
            "Pulled remote changes with {RemovedBoardCount}/{UpsertedBoardCount} boards, {RemovedBikeCount}/{UpsertedBikeCount} bikes, {RemovedSetupCount}/{UpsertedSetupCount} setups, {RemovedTrackCount}/{UpsertedTrackCount} tracks, {RemovedSessionCount}/{UpsertedSessionCount} sessions removed/upserted, and app preferences present {HasAppPreferences}",
            syncData.Boards.Count(board => board.Deleted.HasValue),
            syncData.Boards.Count(board => !board.Deleted.HasValue),
            syncData.Bikes.Count(bike => bike.Deleted.HasValue),
            syncData.Bikes.Count(bike => !bike.Deleted.HasValue),
            syncData.Setups.Count(setup => setup.Deleted.HasValue),
            syncData.Setups.Count(setup => !setup.Deleted.HasValue),
            syncData.Tracks.Count(track => track.Deleted.HasValue),
            syncData.Tracks.Count(track => !track.Deleted.HasValue),
            syncData.Sessions.Count(session => session.Deleted.HasValue),
            syncData.Sessions.Count(session => !session.Deleted.HasValue),
            syncData.AppPreferences is not null);

        return (swaps, syncData.UpperBound);
    }

    private async Task<int> PullIncompleteSessions(IReadOnlyList<SessionBlobSwap> swaps, CancellationToken cancellationToken)
    {
        // A "session blob to pull" is a missing or stale processed BLOB, identified
        // by an (id, target-fingerprint) pair. Two sources, one match-checked
        // download loop:
        //  - fills: rows with no BLOB (data IS NULL); target = the row's own stored
        //    fingerprint, so a fill commits only bytes matching its metadata.
        //  - swaps: rows whose held BLOB has a different current-schema fingerprint
        //    than the one just pulled; target = the accepted remote fingerprint.
        var fills = await sessionRepository.GetIncompleteSessionIdsWithFingerprintAsync();
        var downloadedCount = 0;

        await Parallel.ForEachAsync(fills, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (fill, _) =>
        {
            var (id, fingerprint) = fill;
            if (await TryDownloadAndCommitAsync(id, fingerprint, cancellationToken: cancellationToken))
            {
                Interlocked.Increment(ref downloadedCount);
            }
        });

        // Count swaps that did not commit this run. Fills are re-derived every run from
        // `data IS NULL`, so an unresolved fill is naturally retried; a swap is derived
        // from the transient pulled-metadata delta, and BLOB writes do not bump
        // `updated`, so once the watermark advances past it the swap is never re-derived.
        // The caller therefore holds the watermark back while any swap is unresolved.
        var unresolvedSwaps = 0;
        await Parallel.ForEachAsync(swaps, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (swap, _) =>
        {
            if (await TryDownloadAndCommitAsync(
                    swap.SessionId,
                    swap.TargetFingerprint,
                    swap.TargetGeneration, cancellationToken))
            {
                Interlocked.Increment(ref downloadedCount);
            }
            else
            {
                Interlocked.Increment(ref unresolvedSwaps);
            }
        });

        logger.Verbose(
            "Pulled {DownloadedCount} session blobs ({FillCount} fills, {SwapCount} swaps requested, {UnresolvedSwapCount} swaps unresolved)",
            downloadedCount,
            fills.Count,
            swaps.Count,
            unresolvedSwaps);

        return unresolvedSwaps;
    }

    // Downloads a processed BLOB and commits it only when its fingerprint matches.
    // A 404 (null), fingerprint mismatch, or network error leaves the pull cursor
    // behind so the transient swap is re-derived from the same metadata delta.
    private async Task<bool> TryDownloadAndCommitAsync(
        Guid id,
        string? targetFingerprint,
        SessionProcessedGeneration? targetGeneration = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(targetFingerprint))
        {
            // No current target (legacy / unfingerprinted): defer to the one-time normalization pass.
            return false;
        }

        var transfer = await httpApiService.GetSessionPsstAsync(id, cancellationToken: cancellationToken);
        if (transfer is null)
        {
            return false;
        }

        if (!StringComparer.Ordinal.Equals(transfer.Fingerprint, targetFingerprint))
        {
            logger.Verbose(
                "Skipped session {SessionId}: downloaded fingerprint does not match the target",
                id);
            return false;
        }

        try
        {
            if (await sessionRepository.GetSessionAsync(id) is null) return false;
            if (targetGeneration is null)
                await sessionTelemetryWriter.SwapSessionPsstAsync(id, transfer.Data, transfer.Fingerprint);
            else
                await sessionTelemetryWriter.SwapSessionPsstAsync(id, transfer.Data, transfer.Fingerprint, targetGeneration);

            var fresh = await sessionRepository.GetSessionAsync(id);
            if (fresh is null)
            {
                await sessionStore.PublishSessionsRemovedAsync([id]);
                return false;
            }
            await sessionStore.PublishSessionsChangedAsync([SessionSnapshot.From(fresh)]);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error(e, "Downloaded session data could not be applied for {SessionId}", id);
            return false;
        }
    }

    private async Task PushIncompleteSessionSources(CancellationToken cancellationToken)
    {
        var incompleteSourceIds = await httpApiService.GetIncompleteSessionSourceIdsAsync(cancellationToken: cancellationToken);
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSourceIds, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
        {
            var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);
            if (source is not null)
            {
                await httpApiService.PatchRecordedSessionSourceAsync(ToPayload(source), cancellationToken: cancellationToken);
                Interlocked.Increment(ref uploadedCount);
            }
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete recorded sources out of {IncompleteSourceCount} requested by the server",
            uploadedCount,
            incompleteSourceIds.Count);
    }

    private async Task PullIncompleteSessionSources(CancellationToken cancellationToken)
    {
        var incompleteSourceIds = await recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync();
        var downloadedCount = 0;
        var committedSources = new ConcurrentBag<RecordedSessionSourceSnapshot>();

        try
        {
            await Parallel.ForEachAsync(incompleteSourceIds, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
            {
                var source = await httpApiService.GetRecordedSessionSourceAsync(id, cancellationToken: cancellationToken);
                if (source is not null)
                {
                    if (!RecordedSessionSourceHash.Matches(source))
                    {
                        logger.Warning(
                            "Skipped recorded source {SessionId}: source hash does not match its payload",
                            source.SessionId);
                        return;
                    }

                    var stored = FromPayload(source);
                    await recordedSessionSourceRepository.PutRecordedSessionSourceAsync(stored);
                    committedSources.Add(RecordedSessionSourceSnapshot.From(stored));
                    Interlocked.Increment(ref downloadedCount);
                }
            });
        }
        finally
        {
            if (!committedSources.IsEmpty)
            {
                await sourceStore.PublishSourcesChangedAsync(committedSources.ToArray());
            }
        }

        logger.Verbose(
            "Pulled {DownloadedCount} incomplete recorded sources out of {IncompleteSourceCount} local placeholders",
            downloadedCount,
            incompleteSourceIds.Count);
    }

    private async Task<SynchronizationRunResult> VerifyLocalCompleteness()
    {
        var missingProcessedSessionCount = (await sessionRepository.GetIncompleteSessionIdsAsync()).Count;
        var incompleteRecordedSourceCount = (await recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync()).Count;

        if (missingProcessedSessionCount == 0 && incompleteRecordedSourceCount == 0)
        {
            return new SynchronizationRunResult.Completed();
        }

        logger.Information(
            "Synchronization client run completed with incomplete local data: {MissingProcessedSessionCount} session blob(s) and {IncompleteRecordedSourceCount} recorded source(s) still missing",
            missingProcessedSessionCount,
            incompleteRecordedSourceCount);
        return new SynchronizationRunResult.IncompleteLocalData(
            missingProcessedSessionCount,
            incompleteRecordedSourceCount);
    }

    public async Task<SynchronizationRunResult> SyncAll(IProgress<SynchronizationProgressSnapshot>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var lastPushTime = await syncDataStore.GetLastPushTimeAsync(SyncStateKey);
            var lastPullTime = await syncDataStore.GetLastPullTimeAsync(SyncStateKey);
            var pushSinceExclusive = SynchronizationProtocol.GetSinceExclusive(lastPushTime);
            var pullSinceExclusive = SynchronizationProtocol.GetSinceExclusive(lastPullTime);

            logger.Verbose(
                "Starting synchronization client run with push cursor {LastPushTime} and pull cursor {LastPullTime}",
                lastPushTime,
                lastPullTime);

            // Built by the phase-2 metadata merge and consumed by the phase-4
            // session-data pull. Transient: if any phase throws, the pull cursor below
            // is not advanced and the next run re-derives this set.
            IReadOnlyList<SessionBlobSwap> swaps = [];
            var pullUpperBound = lastPullTime;
            var unresolvedSwaps = 0;

            await RunPhaseAsync(progress, SynchronizationPhase.PushingLocalChanges, "Pushing local changes", 1, () => PushLocalChanges(pushSinceExclusive, cancellationToken), cancellationToken);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingRemoteChanges, "Pulling remote changes", 2, async () =>
            {
                (swaps, pullUpperBound) = await PullRemoteChanges(pullSinceExclusive, cancellationToken);
            }, cancellationToken);

            await RunPhaseAsync(progress, SynchronizationPhase.PushingIncompleteSessions, "Uploading session data", 3, () => PushIncompleteSessions(cancellationToken), cancellationToken);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingIncompleteSessions, "Downloading session data", 4, async () => unresolvedSwaps = await PullIncompleteSessions(swaps, cancellationToken), cancellationToken);

            if (unresolvedSwaps == 0)
            {
                await syncDataStore.UpdateLastPullTimeAsync(SyncStateKey, pullUpperBound);
            }
            else
            {
                logger.Information(
                    "Holding pull cursor: {UnresolvedSwapCount} session-blob swap(s) did not resolve this run and will be retried next sync",
                    unresolvedSwaps);
            }

            await RunPhaseAsync(progress, SynchronizationPhase.PushingIncompleteSessionSources, "Uploading recorded sources", 5, () => PushIncompleteSessionSources(cancellationToken), cancellationToken);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingIncompleteSessionSources, "Downloading recorded sources", 6, () => PullIncompleteSessionSources(cancellationToken), cancellationToken);
            var result = await VerifyLocalCompleteness();

            logger.Verbose("Synchronization client run completed");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (System.Exception exception)
        {
            logger.Error(exception, "Synchronization client run failed");
            throw;
        }
    }

    private static async Task RunPhaseAsync(
        IProgress<SynchronizationProgressSnapshot>? progress,
        SynchronizationPhase phase,
        string message,
        int currentStep,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(progress, phase, message, currentStep);
        await action();
    }

    private static void ReportProgress(
        IProgress<SynchronizationProgressSnapshot>? progress,
        SynchronizationPhase phase,
        string message,
        int currentStep)
    {
        progress?.Report(new SynchronizationProgressSnapshot(
            phase,
            message,
            currentStep,
            TotalSteps: 6,
            IsDeterminate: true));
    }

    private static RecordedSessionSourcePayload ToPayload(RecordedSessionSource source) => new(
        source.SessionId,
        source.SourceKind,
        source.SourceName,
        source.SchemaVersion,
        source.SourceHash,
        source.Payload);

    private static RecordedSessionSource FromPayload(RecordedSessionSourcePayload source) => new()
    {
        SessionId = source.SessionId,
        SourceKind = source.SourceKind,
        SourceName = source.SourceName,
        SchemaVersion = source.SchemaVersion,
        SourceHash = source.SourceHash,
        Payload = source.Payload
    };
}
