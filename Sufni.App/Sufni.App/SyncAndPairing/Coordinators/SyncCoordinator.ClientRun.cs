using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Models;

using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Coordination;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.SyncAndPairing.Coordinators;

public partial class SyncCoordinator
{
    private const int SyncTransferDop = 3;
    private readonly ISyncDataStore syncDataStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionSyncApplier sessionSyncApplier;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery;

    private readonly ISynchronizationApplier rowApplier;

    private async Task PushLocalChanges(ISynchronizationPeer httpApiService, PeerIdentity peer, long sinceExclusive, long upperInclusive, CancellationToken cancellationToken)
    {
        var changes = await syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push,
            sinceExclusive,
            upperInclusive);
        changes.UpperBound = upperInclusive;

        logger.Verbose(
            "Pushing local changes in ({SinceExclusive}, {UpperInclusive}] with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, {TrackCount} tracks",
            sinceExclusive,
            upperInclusive,
            changes.Boards.Count,
            changes.Bikes.Count,
            changes.Setups.Count,
            changes.Sessions.Count,
            changes.Tracks.Count);

        await httpApiService.PushSyncAsync(changes, cancellationToken: cancellationToken);
        await syncDataStore.UpdateLastPushSequenceAsync(peer, upperInclusive);
    }

    private async Task PushIncompleteSessions(ISynchronizationPeer httpApiService, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
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
                if (await httpApiService.PatchSessionPsstAsync(id, blob.Value.Data, blob.Value.Fingerprint, cancellationToken: cancellationToken) == SynchronizationTransferStatus.Completed)
                {
                    Interlocked.Increment(ref uploadedCount);
                    return;
                }
            }
            pending.Add(new(id, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Push));
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete sessions out of {IncompleteSessionCount} requested by the server",
            uploadedCount,
            incompleteSessions.Count);
    }

    private async Task<(IReadOnlyList<SessionBlobSwap> Swaps, long UpperBound)> PullRemoteChanges(ISynchronizationPeer httpApiService, long sinceExclusive, CancellationToken cancellationToken)
    {
        var syncData = await httpApiService.PullSyncAsync(sinceExclusive, cancellationToken: cancellationToken);
        var applied = await rowApplier.ApplyAsync(syncData, SynchronizationDirection.Pull);
        logger.Verbose(
            "Pulled remote changes with {RemovedBoardCount}/{UpsertedBoardCount} boards, {RemovedBikeCount}/{UpsertedBikeCount} bikes, {RemovedSetupCount}/{UpsertedSetupCount} setups, {RemovedTrackCount}/{UpsertedTrackCount} tracks, {RemovedSessionCount}/{UpsertedSessionCount} sessions removed/upserted",
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

        return (applied.Swaps, syncData.UpperBound);
    }

    private async Task<int> PullIncompleteSessions(ISynchronizationPeer httpApiService, IReadOnlyList<SessionBlobSwap> swaps, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
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
            if (await TryDownloadAndCommitAsync(httpApiService, id, fingerprint, cancellationToken: cancellationToken))
            {
                Interlocked.Increment(ref downloadedCount);
            }
            else pending.Add(new(id, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Pull));
        });

        // Count swaps that did not commit this run. Fills are re-derived every run from
        // `data IS NULL`, so an unresolved fill is naturally retried; a swap is derived
        // from the transient pulled-metadata delta, so advancing the watermark
        // can strand a transfer whose remote generation has not changed again.
        // The caller therefore holds the watermark back while any swap is unresolved.
        var unresolvedSwaps = 0;
        await Parallel.ForEachAsync(swaps, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (swap, _) =>
        {
            if (await TryDownloadAndCommitAsync(httpApiService,
                    swap.SessionId,
                    swap.TargetFingerprint,
                    swap.TargetGeneration, cancellationToken))
            {
                Interlocked.Increment(ref downloadedCount);
            }
            else
            {
                Interlocked.Increment(ref unresolvedSwaps);
                pending.Add(new(swap.SessionId, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Pull));
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
    private async Task<bool> TryDownloadAndCommitAsync(ISynchronizationPeer httpApiService,
        Guid id,
        string? targetFingerprint,
        SessionProcessedGeneration? targetGeneration = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(targetFingerprint))
        {
            // No current target (legacy / unfingerprinted): wait for explicit recompute.
            return false;
        }

        var expectedRow = await sessionRepository.GetSessionAsync(id);
        if (expectedRow is null) return false;
        var expected = SessionSnapshot.From(expectedRow);
        var transfer = await httpApiService.GetSessionPsstAsync(id, cancellationToken: cancellationToken);
        if (transfer.Status == SynchronizationTransferStatus.Pending) return false;
        if (transfer.Payload is null) return true;
        var payload = transfer.Payload;

        if (!StringComparer.Ordinal.Equals(payload.Fingerprint, targetFingerprint))
        {
            logger.Verbose(
                "Skipped session {SessionId}: downloaded fingerprint does not match the target",
                id);
            return false;
        }

        return await sessionSyncApplier.ApplyDownloadedAsync(
            expected, payload, targetFingerprint, targetGeneration) is SessionDataApplyResult.Applied or SessionDataApplyResult.Ignored;
    }

    private async Task PushIncompleteSessionSources(ISynchronizationPeer httpApiService, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
    {
        var incompleteSourceIds = await httpApiService.GetIncompleteSessionSourceIdsAsync(cancellationToken: cancellationToken);
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSourceIds, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
        {
            var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);
            if (source is not null)
            {
                if (await httpApiService.PatchRecordedSessionSourceAsync(ToPayload(source), cancellationToken: cancellationToken) == SynchronizationTransferStatus.Completed)
                {
                    Interlocked.Increment(ref uploadedCount);
                    return;
                }
            }
            pending.Add(new(id, SynchronizationTransferKind.RecordedSource, SynchronizationDirection.Push));
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete recorded sources out of {IncompleteSourceCount} requested by the server",
            uploadedCount,
            incompleteSourceIds.Count);
    }

    private async Task PullIncompleteSessionSources(ISynchronizationPeer httpApiService, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
    {
        var incompleteSourceIds = await recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync();
        var downloadedCount = 0;
        var committedSources = new ConcurrentBag<RecordedSessionSourceSnapshot>();

        try
        {
            await Parallel.ForEachAsync(incompleteSourceIds, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
            {
                var transfer = await httpApiService.GetRecordedSessionSourceAsync(id, cancellationToken: cancellationToken);
                if (transfer.Status == SynchronizationTransferStatus.Pending)
                {
                    pending.Add(new(id, SynchronizationTransferKind.RecordedSource, SynchronizationDirection.Pull));
                    return;
                }
                if (transfer.Payload is { } source)
                {
                    try
                    {
                        if (!RecordedSessionSourceHash.Matches(source))
                        {
                            logger.Warning(
                                "Skipped recorded source {SessionId}: source hash does not match its payload",
                                source.SessionId);
                            pending.Add(new(id, SynchronizationTransferKind.RecordedSource, SynchronizationDirection.Pull));
                            return;
                        }
                    }
                    catch (Exception exception) when (exception is System.Text.Json.JsonException or ZstdSharp.ZstdException)
                    {
                        logger.Warning(exception, "Skipped invalid recorded source payload for {SessionId}", source.SessionId);
                        pending.Add(new(id, SynchronizationTransferKind.RecordedSource, SynchronizationDirection.Pull));
                        return;
                    }

                    var stored = await recordedSessionSourceRepository.PutRecordedSessionSourceAsync(FromPayload(source));
                    committedSources.Add(stored);
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

    private async Task<SynchronizationRunResult> VerifyLocalCompleteness(ConcurrentBag<SynchronizationPendingTransfer> pending)
    {
        var missingProcessedSessionCount = (await sessionRepository.GetIncompleteSessionIdsAsync()).Count;
        var incompleteRecordedSourceCount = (await recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync()).Count;

        if (missingProcessedSessionCount == 0 && incompleteRecordedSourceCount == 0 && pending.IsEmpty)
        {
            return new SynchronizationRunResult.Completed();
        }

        logger.Information(
            "Synchronization client run completed with incomplete local data: {MissingProcessedSessionCount} session blob(s) and {IncompleteRecordedSourceCount} recorded source(s) still missing",
            missingProcessedSessionCount,
            incompleteRecordedSourceCount);
        return new SynchronizationRunResult.IncompleteData(
            missingProcessedSessionCount,
            incompleteRecordedSourceCount, pending.ToArray());
    }

    private async Task<SynchronizationRunResult> RunClientAsync(ISynchronizationPeer httpApiService, CancellationToken cancellationToken = default)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, httpApiService.Lifetime);
        cancellationToken = run.Token;
        PeerIdentity? peer = null;
        try
        {
            var pushUpperBound = await syncDataStore.GetSequenceUpperBoundAsync(SynchronizationDirection.Push);
            peer = await httpApiService.GetPeerIdentityAsync();
            var lastPushSequence = await syncDataStore.GetLastPushSequenceAsync(peer);
            var lastPullSequence = await syncDataStore.GetLastPullSequenceAsync(peer);
            var pushSinceExclusive = lastPushSequence;
            var pullSinceExclusive = lastPullSequence;

            logger.Verbose(
                "Starting synchronization client run with push cursor {LastPushSequence} and pull cursor {LastPullSequence}",
                lastPushSequence,
                lastPullSequence);

            // Built by the metadata merge and consumed by the phase-4
            // session-data pull. Transient: if any phase throws, the pull cursor below
            // is not advanced and the next run re-derives this set.
            IReadOnlyList<SessionBlobSwap> swaps = [];
            var pullUpperBound = lastPullSequence;
            var unresolvedSwaps = 0;
            var pending = new ConcurrentBag<SynchronizationPendingTransfer>();

            (SynchronizationPhase Phase, string Message, Func<Task> Run) push =
                (SynchronizationPhase.PushingLocalChanges, "Pushing local changes", () => PushLocalChanges(httpApiService, peer, pushSinceExclusive, pushUpperBound, cancellationToken));
            (SynchronizationPhase Phase, string Message, Func<Task> Run) pull =
                (SynchronizationPhase.PullingRemoteChanges, "Pulling remote changes", async () =>
                {
                    (swaps, pullUpperBound) = await PullRemoteChanges(httpApiService, pullSinceExclusive, cancellationToken);
                });
            var rowPhases = lastPushSequence == 0 ? new[] { pull, push } : new[] { push, pull };
            for (var index = 0; index < rowPhases.Length; index++)
                await RunPhaseAsync( rowPhases[index].Phase, rowPhases[index].Message, index + 1, rowPhases[index].Run, cancellationToken);

            await RunPhaseAsync( SynchronizationPhase.PushingIncompleteSessions, "Uploading session data", 3, () => PushIncompleteSessions(httpApiService, pending, cancellationToken), cancellationToken);
            await RunPhaseAsync( SynchronizationPhase.PullingIncompleteSessions, "Downloading session data", 4, async () => unresolvedSwaps = await PullIncompleteSessions(httpApiService, swaps, pending, cancellationToken), cancellationToken);

            if (unresolvedSwaps == 0)
            {
                await syncDataStore.UpdateLastPullSequenceAsync(peer, pullUpperBound);
            }
            else
            {
                logger.Information(
                    "Holding pull cursor: {UnresolvedSwapCount} session-blob swap(s) did not resolve this run and will be retried next sync",
                    unresolvedSwaps);
            }

            await RunPhaseAsync( SynchronizationPhase.PushingIncompleteSessionSources, "Uploading recorded sources", 5, () => PushIncompleteSessionSources(httpApiService, pending, cancellationToken), cancellationToken);
            await RunPhaseAsync( SynchronizationPhase.PullingIncompleteSessionSources, "Downloading recorded sources", 6, () => PullIncompleteSessionSources(httpApiService, pending, cancellationToken), cancellationToken);
            var result = await VerifyLocalCompleteness(pending);

            logger.Verbose("Synchronization client run completed");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (System.Exception exception)
        {
            logger.Error(exception, "Synchronization client run failed for {Peer}", peer);
            var reason = exception is System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.UpgradeRequired }
                ? SyncFailureReason.ProtocolMismatch
                : SyncFailureReason.Synchronize;
            return new SynchronizationRunResult.Failed(reason, exception.Message);
        }
    }

    private async Task RunPhaseAsync(
        SynchronizationPhase phase,
        string message,
        int currentStep,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await uiThreadDispatcher.InvokeAsync(() => Progress = new SynchronizationProgressSnapshot(phase, message, currentStep + 1, 7, true));
        await action();
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
