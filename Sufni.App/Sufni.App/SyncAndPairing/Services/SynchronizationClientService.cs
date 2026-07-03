using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Sync;
using Serilog;

using Sufni.App.Extensibility.Sync;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
namespace Sufni.App.SyncAndPairing.Services;

public class SynchronizationClientService : ISynchronizationClientService
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationClientService>();
    public const string SyncStateKey = "paired-server";

    private readonly ISyncDataStore syncDataStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery;
    private readonly IHttpApiService httpApiService;
    private readonly IAppPreferences appPreferences;
    private readonly IExtensionSyncService? extensionSyncService;

    public SynchronizationClientService(
        ISyncDataStore syncDataStore,
        ISessionRepository sessionRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        IHttpApiService httpApiService,
        IAppPreferences appPreferences)
        : this(syncDataStore, sessionRepository, sessionTelemetryWriter, recordedSessionSourceRepository, recordedSessionSourceSyncQuery, httpApiService, appPreferences, null)
    {
    }

    internal SynchronizationClientService(
        ISyncDataStore syncDataStore,
        ISessionRepository sessionRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        IHttpApiService httpApiService,
        IAppPreferences appPreferences,
        IExtensionSyncService? extensionSyncService)
    {
        this.syncDataStore = syncDataStore;
        this.sessionRepository = sessionRepository;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.recordedSessionSourceSyncQuery = recordedSessionSourceSyncQuery;
        this.httpApiService = httpApiService;
        this.appPreferences = appPreferences;
        this.extensionSyncService = extensionSyncService;
    }

    private async Task PushLocalChanges(long lastSyncTime)
    {
        var changes = await syncDataStore.GetSynchronizationDataAsync(lastSyncTime);
        changes.AppPreferences = await appPreferences.GetSyncDataAsync(lastSyncTime);
        if (extensionSyncService is not null)
        {
            changes.ExtensionBatches.AddRange(await extensionSyncService.CreateBatchesAsync(lastSyncTime));
        }

        logger.Verbose(
            "Pushing local changes since {LastSyncTime} with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, {TrackCount} tracks, {ExtensionBatchCount} extension batches, and app preferences present {HasAppPreferences}",
            lastSyncTime,
            changes.Boards.Count,
            changes.Bikes.Count,
            changes.Setups.Count,
            changes.Sessions.Count,
            changes.Tracks.Count,
            changes.ExtensionBatches.Count,
            changes.AppPreferences is not null);

        await httpApiService.PushSyncAsync(changes);
    }

    private async Task PushIncompleteSessions()
    {
        var incompleteSessions = await httpApiService.GetIncompleteSessionIdsAsync();
        var uploadedCount = 0;

        foreach (var id in incompleteSessions)
        {
            var blob = await sessionRepository.GetSessionRawPsstWithFingerprintAsync(id);
            if (blob is not null)
            {
                // Upload the bytes with their fingerprint so the hub rejects a
                // mismatch instead of storing bytes that contradict its metadata.
                await httpApiService.PatchSessionPsstAsync(id, blob.Value.Data, blob.Value.Fingerprint);
                uploadedCount++;
            }
        }

        logger.Verbose(
            "Pushed {UploadedCount} incomplete sessions out of {IncompleteSessionCount} requested by the server",
            uploadedCount,
            incompleteSessions.Count);
    }

    private async Task<IReadOnlyList<SessionBlobSwap>> PullRemoteChanges(
        long lastSyncTime,
        IProgress<SynchronizationProgressSnapshot>? progress)
    {
        var syncData = await httpApiService.PullSyncAsync(lastSyncTime);
        var swaps = await syncDataStore.ApplyRemoteSynchronizationDataAsync(syncData);
        await appPreferences.ApplySyncDataAsync(syncData.AppPreferences);
        if (extensionSyncService is not null)
        {
            var extensionProgress = await extensionSyncService.ApplyBatchesAsync(
                syncData.ExtensionBatches,
                SynchronizationPhase.PullingRemoteChanges,
                currentStep: 2,
                totalSteps: 6);
            foreach (var snapshot in extensionProgress)
            {
                progress?.Report(snapshot);
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

        return swaps;
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

        foreach (var (id, fingerprint) in fills)
        {
            if (await TryDownloadAndCommitAsync(id, fingerprint))
            {
                downloadedCount++;
            }
        }

        // Count swaps that did not commit this run. Fills are re-derived every run from
        // `data IS NULL`, so an unresolved fill is naturally retried; a swap is derived
        // from the transient pulled-metadata delta, and BLOB writes do not bump
        // `updated`, so once the watermark advances past it the swap is never re-derived.
        // The caller therefore holds the watermark back while any swap is unresolved.
        var unresolvedSwaps = 0;
        foreach (var swap in swaps)
        {
            if (await TryDownloadAndCommitAsync(swap.SessionId, swap.TargetFingerprint))
            {
                downloadedCount++;
            }
            else
            {
                unresolvedSwaps++;
            }
        }

        logger.Verbose(
            "Pulled {DownloadedCount} session blobs ({FillCount} fills, {SwapCount} swaps requested, {UnresolvedSwapCount} swaps unresolved)",
            downloadedCount,
            fills.Count,
            swaps.Count,
            unresolvedSwaps);

        return unresolvedSwaps;
    }

    // Downloads a processed BLOB and commits it only when its fingerprint matches
    // the target. A 404 (null) or a fingerprint mismatch is "resolved for now": the
    // run may still advance its single last-sync watermark and re-detect later. A
    // network error propagates, so the watermark does NOT advance and the transient
    // swap set is re-derived from the same metadata delta next run.
    private async Task<bool> TryDownloadAndCommitAsync(Guid id, string? targetFingerprint)
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

        await sessionTelemetryWriter.SwapSessionPsstAsync(id, transfer.Data, transfer.Fingerprint);
        return true;
    }

    private async Task PushIncompleteSessionSources()
    {
        var incompleteSourceIds = await httpApiService.GetIncompleteSessionSourceIdsAsync();
        var uploadedCount = 0;

        foreach (var id in incompleteSourceIds)
        {
            var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);
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
        var incompleteSourceIds = await recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync();
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

                await recordedSessionSourceRepository.PutRecordedSessionSourceAsync(FromTransfer(source));
                downloadedCount++;
            }
        }

        logger.Verbose(
            "Pulled {DownloadedCount} incomplete recorded sources out of {IncompleteSourceCount} local placeholders",
            downloadedCount,
            incompleteSourceIds.Count);
    }

    public async Task SyncAll(IProgress<SynchronizationProgressSnapshot>? progress = null)
    {
        try
        {
            var lastSyncTime = await syncDataStore.GetLastSyncTimeAsync(SyncStateKey);

            logger.Verbose("Starting synchronization client run with last sync time {LastSyncTime}", lastSyncTime);

            // Built by the phase-2 metadata merge and consumed by the phase-4
            // session-data pull. Transient: if any phase throws, the watermark below
            // is not advanced and the next run re-derives this set.
            IReadOnlyList<SessionBlobSwap> swaps = [];
            var unresolvedSwaps = 0;

            await RunPhaseAsync(progress, SynchronizationPhase.PushingLocalChanges, "Pushing local changes", 1, () => PushLocalChanges(lastSyncTime));
            await RunPhaseAsync(progress, SynchronizationPhase.PullingRemoteChanges, "Pulling remote changes", 2, async () => swaps = await PullRemoteChanges(lastSyncTime, progress));
            await RunPhaseAsync(progress, SynchronizationPhase.PushingIncompleteSessions, "Uploading session data", 3, PushIncompleteSessions);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingIncompleteSessions, "Downloading session data", 4, async () => unresolvedSwaps = await PullIncompleteSessions(swaps));
            await RunPhaseAsync(progress, SynchronizationPhase.PushingIncompleteSessionSources, "Uploading recorded sources", 5, PushIncompleteSessionSources);
            await RunPhaseAsync(progress, SynchronizationPhase.PullingIncompleteSessionSources, "Downloading recorded sources", 6, PullIncompleteSessionSources);

            // Only advance the single sync watermark when every swap committed. A swap is
            // derived from the pulled-metadata delta and BLOB writes do not bump `updated`,
            // so advancing past an unresolved swap would strand it permanently. Holding the
            // watermark re-pulls the same delta next run, re-derives the swap, and retries.
            if (unresolvedSwaps == 0)
            {
                await syncDataStore.UpdateLastSyncTimeAsync(SyncStateKey);
                logger.Verbose("Synchronization client run completed");
            }
            else
            {
                logger.Information(
                    "Holding sync watermark: {UnresolvedSwapCount} session-blob swap(s) did not resolve this run and will be retried next sync",
                    unresolvedSwaps);
            }
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
