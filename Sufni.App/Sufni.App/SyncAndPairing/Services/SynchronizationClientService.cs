using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Extensibility.Sync;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
namespace Sufni.App.SyncAndPairing.Services;

public class SynchronizationClientService : ISynchronizationClientService
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationClientService>();
    public const string SyncStateKey = "paired-server";
    private const int SyncTransferDop = 3;
    private static readonly ParallelOptions SyncTransferParallelOptions = new()
    {
        MaxDegreeOfParallelism = SyncTransferDop
    };

    private readonly ISyncDataStore syncDataStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionStoreWriter sessionStore;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery;
    private readonly IHttpApiService httpApiService;
    private readonly IAppPreferences appPreferences;
    private readonly IExtensionSyncService? extensionSyncService;

    public SynchronizationClientService(
        ISyncDataStore syncDataStore,
        ISessionRepository sessionRepository,
        ISessionStoreWriter sessionStore,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        IHttpApiService httpApiService,
        IAppPreferences appPreferences)
        : this(syncDataStore, sessionRepository, sessionStore, recordedSessionSourceRepository, sourceStore, recordedSessionSourceSyncQuery, httpApiService, appPreferences, null)
    {
    }

    internal SynchronizationClientService(
        ISyncDataStore syncDataStore,
        ISessionRepository sessionRepository,
        ISessionStoreWriter sessionStore,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        IHttpApiService httpApiService,
        IAppPreferences appPreferences,
        IExtensionSyncService? extensionSyncService)
    {
        this.syncDataStore = syncDataStore;
        this.sessionRepository = sessionRepository;
        this.sessionStore = sessionStore;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionSourceSyncQuery = recordedSessionSourceSyncQuery;
        this.httpApiService = httpApiService;
        this.appPreferences = appPreferences;
        this.extensionSyncService = extensionSyncService;
    }

    private async Task PushLocalChanges(long sinceExclusive)
    {
        var upperInclusive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changes = await syncDataStore.GetSynchronizationDataAsync(
            sinceExclusive,
            upperInclusive);
        changes.UpperBound = upperInclusive;
        changes.AppPreferences = await appPreferences.GetSyncDataAsync(
            sinceExclusive,
            upperInclusive);
        if (extensionSyncService is not null)
        {
            changes.ExtensionBatches.AddRange(await extensionSyncService.CreateBatchesAsync(
                sinceExclusive,
                upperInclusive));
        }

        logger.Verbose(
            "Pushing local changes in ({SinceExclusive}, {UpperInclusive}] with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, {TrackCount} tracks, {ExtensionBatchCount} extension batches, and app preferences present {HasAppPreferences}",
            sinceExclusive,
            upperInclusive,
            changes.Boards.Count,
            changes.Bikes.Count,
            changes.Setups.Count,
            changes.Sessions.Count,
            changes.Tracks.Count,
            changes.ExtensionBatches.Count,
            changes.AppPreferences is not null);

        await httpApiService.PushSyncAsync(changes);
        await syncDataStore.UpdateLastPushTimeAsync(SyncStateKey, upperInclusive);
    }

    private async Task PushIncompleteSessions()
    {
        var incompleteSessions = await httpApiService.GetIncompleteSessionIdsAsync();
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSessions, SyncTransferParallelOptions, async (id, _) =>
        {
            var blob = await sessionRepository.GetSessionRawPsstWithFingerprintAsync(id);
            if (blob is not null)
            {
                // Upload the bytes with their fingerprint so the hub rejects a
                // mismatch instead of storing bytes that contradict its metadata.
                await httpApiService.PatchSessionPsstAsync(id, blob.Value.Data, blob.Value.Fingerprint);
                Interlocked.Increment(ref uploadedCount);
            }
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete sessions out of {IncompleteSessionCount} requested by the server",
            uploadedCount,
            incompleteSessions.Count);
    }

    private async Task<(IReadOnlyList<SessionBlobSwap> Swaps, long UpperBound, string? PartialApplyError)> PullRemoteChanges(
        long sinceExclusive,
        IProgress<SynchronizationProgressSnapshot>? progress)
    {
        var syncData = await httpApiService.PullSyncAsync(sinceExclusive);
        var extensionPlan = extensionSyncService is null
            ? null
            : await extensionSyncService.PrepareBatchesAsync(syncData.ExtensionBatches);
        var swaps = await syncDataStore.ApplyRemoteSynchronizationDataAsync(syncData);
        await appPreferences.ApplySyncDataAsync(syncData.AppPreferences);
        if (extensionSyncService is not null && extensionPlan is not null)
        {
            try
            {
                var extensionProgress = await extensionSyncService.ApplyPreparedBatchesAsync(
                    extensionPlan,
                    SynchronizationPhase.PullingRemoteChanges,
                    currentStep: 2,
                    totalSteps: 6);
                foreach (var snapshot in extensionProgress)
                {
                    progress?.Report(snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Extension synchronization failed after pulled core data was persisted");
                return (swaps, syncData.UpperBound, exception.Message);
            }
        }

        logger.Verbose(
            "Pulled remote changes with {RemovedBoardCount}/{UpsertedBoardCount} boards, {RemovedBikeCount}/{UpsertedBikeCount} bikes, {RemovedSetupCount}/{UpsertedSetupCount} setups, {RemovedTrackCount}/{UpsertedTrackCount} tracks, {RemovedSessionCount}/{UpsertedSessionCount} sessions removed/upserted, {ExtensionBatchCount} extension batches, and app preferences present {HasAppPreferences}",
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
            syncData.ExtensionBatches.Count,
            syncData.AppPreferences is not null);

        return (swaps, syncData.UpperBound, null);
    }

    private async Task<int> PullIncompleteSessions(IReadOnlyList<SessionBlobSwap> swaps)
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

        await Parallel.ForEachAsync(fills, SyncTransferParallelOptions, async (fill, _) =>
        {
            var (id, fingerprint) = fill;
            if (await TryDownloadAndCommitAsync(id, fingerprint))
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
        await Parallel.ForEachAsync(swaps, SyncTransferParallelOptions, async (swap, _) =>
        {
            if (await TryDownloadAndCommitAsync(
                    swap.SessionId,
                    swap.TargetFingerprint,
                    swap.TargetGeneration))
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
        SessionProcessedGeneration? targetGeneration = null)
    {
        if (string.IsNullOrEmpty(targetFingerprint))
        {
            // No current target (legacy / unfingerprinted): defer to the one-time normalization pass.
            return false;
        }

        var transfer = await httpApiService.GetSessionPsstAsync(id);
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

        var result = targetGeneration is null
            ? await sessionStore.CommitPsstSwapAsync(id, transfer.Data, transfer.Fingerprint)
            : await sessionStore.CommitPsstSwapAsync(
                id,
                transfer.Data,
                transfer.Fingerprint,
                targetGeneration);
        return result is StoreMutationResult<SessionSnapshot>.Saved;
    }

    private async Task PushIncompleteSessionSources()
    {
        var incompleteSourceIds = await httpApiService.GetIncompleteSessionSourceIdsAsync();
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSourceIds, SyncTransferParallelOptions, async (id, _) =>
        {
            var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);
            if (source is not null)
            {
                await httpApiService.PatchRecordedSessionSourceAsync(ToPayload(source));
                Interlocked.Increment(ref uploadedCount);
            }
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete recorded sources out of {IncompleteSourceCount} requested by the server",
            uploadedCount,
            incompleteSourceIds.Count);
    }

    private async Task PullIncompleteSessionSources()
    {
        var incompleteSourceIds = await recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync();
        var downloadedCount = 0;
        var committedSourceIds = new ConcurrentBag<Guid>();

        await Parallel.ForEachAsync(incompleteSourceIds, SyncTransferParallelOptions, async (id, _) =>
        {
            var source = await httpApiService.GetRecordedSessionSourceAsync(id);
            if (source is not null)
            {
                if (!RecordedSessionSourceHash.Matches(source))
                {
                    logger.Warning(
                        "Skipped recorded source {SessionId}: source hash does not match its payload",
                        source.SessionId);
                    return;
                }

                await recordedSessionSourceRepository.PutRecordedSessionSourceAsync(FromPayload(source));
                committedSourceIds.Add(source.SessionId);
                Interlocked.Increment(ref downloadedCount);
            }
        });

        if (!committedSourceIds.IsEmpty)
        {
            await sourceStore.PublishSourcesChangedAsync(committedSourceIds.ToArray());
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

    public async Task<SynchronizationRunResult> SyncAll(IProgress<SynchronizationProgressSnapshot>? progress = null)
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
            string? partialApplyError = null;

            await RunPhaseAsync(progress, SynchronizationPhase.PushingLocalChanges, "Pushing local changes", 1, () => PushLocalChanges(pushSinceExclusive));
            await RunPhaseAsync(progress, SynchronizationPhase.PullingRemoteChanges, "Pulling remote changes", 2, async () =>
            {
                (swaps, pullUpperBound, partialApplyError) = await PullRemoteChanges(pullSinceExclusive, progress);
            });
            if (partialApplyError is not null)
            {
                return new SynchronizationRunResult.PartialApply(partialApplyError);
            }

            await RunPhaseAsync(progress, SynchronizationPhase.PushingIncompleteSessions, "Uploading session data", 3, PushIncompleteSessions);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingIncompleteSessions, "Downloading session data", 4, async () => unresolvedSwaps = await PullIncompleteSessions(swaps));

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

            await RunPhaseAsync(progress, SynchronizationPhase.PushingIncompleteSessionSources, "Uploading recorded sources", 5, PushIncompleteSessionSources);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingIncompleteSessionSources, "Downloading recorded sources", 6, PullIncompleteSessionSources);
            var result = await VerifyLocalCompleteness();

            logger.Verbose("Synchronization client run completed");
            return result;
        }
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
        Func<Task> action)
    {
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
