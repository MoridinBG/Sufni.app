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

    private async Task PushIncompleteSessions(ISynchronizationPeer httpApiService, IReadOnlyList<Guid> incompleteSessions,
        Func<Task> itemChecked, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
    {
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSessions, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
        {
            var blob = await sessionRepository.GetSessionRawPsstWithFingerprintAsync(id);
            // Upload the fingerprint with the bytes so the hub can validate the generation.
            if (blob is not null && await httpApiService.PatchSessionPsstAsync(id, blob.Value.Data, blob.Value.Fingerprint,
                    cancellationToken: cancellationToken) == SynchronizationTransferStatus.Completed)
                Interlocked.Increment(ref uploadedCount);
            else
                pending.Add(new(id, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Push));
            await itemChecked();
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

    private async Task<int> PullIncompleteSessions(ISynchronizationPeer httpApiService, IReadOnlyList<(Guid Id, string? Fingerprint)> fills,
        IReadOnlyList<SessionBlobSwap> swaps, Func<Task> itemChecked, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
    {
        // A "session blob to pull" is a missing or stale processed BLOB, identified
        // by an (id, target-fingerprint) pair. Two sources, one match-checked
        // download loop:
        //  - fills: rows with no BLOB (data IS NULL); target = the row's own stored
        //    fingerprint, so a fill commits only bytes matching its metadata.
        //  - swaps: rows whose held BLOB has a different current-schema fingerprint
        //    than the one just pulled; target = the accepted remote fingerprint.
        var downloadedCount = 0;

        foreach (var (id, fingerprint) in fills)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryDownloadAndCommitAsync(httpApiService, id, fingerprint, cancellationToken: cancellationToken))
            {
                downloadedCount++;
            }
            else pending.Add(new(id, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Pull));
            await itemChecked();
        }

        // Count swaps that did not commit this run. Fills are re-derived every run from
        // `data IS NULL`, so an unresolved fill is naturally retried; a swap is derived
        // from the transient pulled-metadata delta, so advancing the watermark
        // can strand a transfer whose remote generation has not changed again.
        // The caller therefore holds the watermark back while any swap is unresolved.
        var unresolvedSwaps = 0;
        foreach (var swap in swaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryDownloadAndCommitAsync(httpApiService,
                    swap.SessionId,
                    swap.TargetFingerprint,
                    swap.TargetGeneration, cancellationToken))
            {
                downloadedCount++;
            }
            else
            {
                unresolvedSwaps++;
                pending.Add(new(swap.SessionId, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Pull));
            }
            await itemChecked();
        }

        logger.Verbose(
            "Pulled {DownloadedCount} session blobs ({FillCount} fills, {SwapCount} swaps requested, {UnresolvedSwapCount} swaps unresolved)",
            downloadedCount,
            fills.Count,
            swaps.Count,
            unresolvedSwaps);

        return unresolvedSwaps;
    }

    // Downloads a processed BLOB and commits it only when its fingerprint matches.
    // A 404, fingerprint mismatch, or network error leaves the pull cursor
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
        return transfer switch
        {
            SynchronizationTransferResult<SessionBlobPayload>.Pending => false,
            SynchronizationTransferResult<SessionBlobPayload>.Ignored => true,
            SynchronizationTransferResult<SessionBlobPayload>.Received(var payload) =>
                await CommitPayloadAsync(payload)
        };

        async Task<bool> CommitPayloadAsync(SessionBlobPayload payload)
        {
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
    }

    private async Task PushIncompleteSessionSources(ISynchronizationPeer httpApiService, IReadOnlyList<Guid> incompleteSourceIds,
        Func<Task> itemChecked, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
    {
        var uploadedCount = 0;

        await Parallel.ForEachAsync(incompleteSourceIds, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
        {
            var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);
            if (source is not null && await httpApiService.PatchRecordedSessionSourceAsync(ToPayload(source),
                    cancellationToken: cancellationToken) == SynchronizationTransferStatus.Completed)
                Interlocked.Increment(ref uploadedCount);
            else
                pending.Add(new(id, SynchronizationTransferKind.RecordedSource, SynchronizationDirection.Push));
            await itemChecked();
        });

        logger.Verbose(
            "Pushed {UploadedCount} incomplete recorded sources out of {IncompleteSourceCount} requested by the server",
            uploadedCount,
            incompleteSourceIds.Count);
    }

    private async Task PullIncompleteSessionSources(ISynchronizationPeer httpApiService, IReadOnlyList<Guid> incompleteSourceIds,
        Func<Task> itemChecked, ConcurrentBag<SynchronizationPendingTransfer> pending, CancellationToken cancellationToken)
    {
        var committedSources = new ConcurrentBag<RecordedSessionSourceSnapshot>();

        try
        {
            await Parallel.ForEachAsync(incompleteSourceIds, new ParallelOptions { MaxDegreeOfParallelism = SyncTransferDop, CancellationToken = cancellationToken }, async (id, _) =>
            {
                var transfer = await httpApiService.GetRecordedSessionSourceAsync(id, cancellationToken: cancellationToken);
                var complete = transfer switch
                {
                    SynchronizationTransferResult<RecordedSessionSourcePayload>.Pending => false,
                    SynchronizationTransferResult<RecordedSessionSourcePayload>.Ignored => true,
                    SynchronizationTransferResult<RecordedSessionSourcePayload>.Received(var source) =>
                        await StoreVerifiedSourceAsync(source)
                };
                if (!complete)
                    pending.Add(new(id, SynchronizationTransferKind.RecordedSource, SynchronizationDirection.Pull));
                await itemChecked();
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
            committedSources.Count,
            incompleteSourceIds.Count);

        async Task<bool> StoreVerifiedSourceAsync(RecordedSessionSourcePayload source)
        {
            try
            {
                if (!RecordedSessionSourceHash.Matches(source))
                {
                    logger.Warning("Skipped recorded source {SessionId}: source hash does not match its payload", source.SessionId);
                    return false;
                }
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or System.IO.InvalidDataException)
            {
                logger.Warning(exception, "Skipped invalid recorded source payload for {SessionId}", source.SessionId);
                return false;
            }

            var stored = await recordedSessionSourceRepository.PutRecordedSessionSourceAsync(FromPayload(source));
            committedSources.Add(stored);
            return true;
        }
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
            var runId = Guid.NewGuid();
            long lastPushSequence;
            long lastPullSequence;
            SynchronizationPhase[] phases;
            for (var attempt = 0; ; attempt++)
            {
                lastPushSequence = await syncDataStore.GetLastPushSequenceAsync(peer);
                lastPullSequence = await syncDataStore.GetLastPullSequenceAsync(peer);
                phases = SynchronizationRunStart.OrderedPhases(pullFirst: lastPushSequence == 0);
                var verified = await httpApiService.BeginRunAsync(new SynchronizationRunStart(
                    runId, peer.DatabaseIncarnation, phases), cancellationToken);
                if (verified == peer) break;
                if (attempt == 1) throw new System.Net.Http.HttpRequestException("The hub identity changed during run negotiation.");
                peer = verified;
            }

            IReadOnlyList<SessionBlobSwap> swaps = [];
            var pullUpperBound = lastPullSequence;
            foreach (var phase in phases.Take(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await BeginPhaseAsync(httpApiService, phases, phase);
                if (phase == SynchronizationPhase.PushingLocalChanges)
                    await PushLocalChanges(httpApiService, peer, lastPushSequence, pushUpperBound, cancellationToken);
                else
                    (swaps, pullUpperBound) = await PullRemoteChanges(httpApiService, lastPullSequence, cancellationToken);
            }

            var result = await TransferAndVerifyAsync(httpApiService, phases, peer, swaps, pullUpperBound, cancellationToken);
            await httpApiService.CompleteRunAsync(cancellationToken);
            await uiThreadDispatcher.InvokeAsync(() => Progress = SynchronizationProgressSnapshot.Verified);
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
        finally { httpApiService.ClearRun(); }
    }

    private Task BeginPhaseAsync(ISynchronizationPeer transport, SynchronizationPhase[] phases, SynchronizationPhase phase)
    {
        transport.SetRunPhase(phase);
        return uiThreadDispatcher.InvokeAsync(() => Progress = SynchronizationProgressSnapshot.ForPhase(phases, phase));
    }

    private async Task<SynchronizationRunResult> TransferAndVerifyAsync(ISynchronizationPeer transport, SynchronizationPhase[] phases,
        PeerIdentity peer, IReadOnlyList<SessionBlobSwap> swaps, long pullUpperBound, CancellationToken cancellationToken)
    {
        await BeginPhaseAsync(transport, phases, SynchronizationPhase.PlanningTransfers);
        // Snapshot only identities and target fingerprints. Read each payload when its turn comes.
        var sourceUploads = await transport.GetIncompleteSessionSourceIdsAsync(cancellationToken);
        var sourceDownloads = await recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync();
        var sessionUploads = await transport.GetIncompleteSessionIdsAsync(cancellationToken);
        var fills = await sessionRepository.GetIncompleteSessionIdsWithFingerprintAsync();
        var sessionDownloads = checked(fills.Count + swaps.Count);
        var total = checked(sourceUploads.Count + sourceDownloads.Count + sessionUploads.Count + sessionDownloads);
        var pending = new ConcurrentBag<SynchronizationPendingTransfer>();
        var previous = 0;
        var unresolved = 0;
        using var reporting = new SemaphoreSlim(1);

        await RunBatchAsync(SynchronizationPhase.PushingIncompleteSessionSources, sourceUploads.Count,
            report => PushIncompleteSessionSources(transport, sourceUploads, report, pending, cancellationToken));
        await RunBatchAsync(SynchronizationPhase.PullingIncompleteSessionSources, sourceDownloads.Count,
            report => PullIncompleteSessionSources(transport, sourceDownloads, report, pending, cancellationToken));
        await RunBatchAsync(SynchronizationPhase.PushingIncompleteSessions, sessionUploads.Count,
            report => PushIncompleteSessions(transport, sessionUploads, report, pending, cancellationToken));
        await RunBatchAsync(SynchronizationPhase.PullingIncompleteSessions, sessionDownloads,
            async report => { unresolved = await PullIncompleteSessions(transport, fills, swaps, report, pending, cancellationToken); });

        cancellationToken.ThrowIfCancellationRequested();
        transport.SetRunPhase(SynchronizationPhase.Verifying);
        await PublishAsync(SynchronizationPhase.Verifying, new(0, 0, previous, total));
        if (unresolved == 0) await syncDataStore.UpdateLastPullSequenceAsync(peer, pullUpperBound);
        else logger.Information("Holding pull cursor for {UnresolvedSwapCount} pending session generations", unresolved);
        return await VerifyLocalCompleteness(pending);

        async Task RunBatchAsync(SynchronizationPhase phase, int count, Func<Func<Task>, Task> transfer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count == 0) return;
            transport.SetRunPhase(phase);
            var handled = 0;
            await PublishAsync(phase, new(0, count, previous, total));
            await transfer(async () =>
            {
                // Parallel transfers settle independently. Publish their observations in order on both devices.
                await reporting.WaitAsync(cancellationToken);
                try { await PublishAsync(phase, new(++handled, count, previous, total)); }
                finally { reporting.Release(); }
            });
            previous += count;
        }

        async Task PublishAsync(SynchronizationPhase phase, SynchronizationItemProgress items)
        {
            await uiThreadDispatcher.InvokeAsync(() => Progress = SynchronizationProgressSnapshot.ForPhase(phases, phase, items));
            await transport.ReportItemProgressAsync(items, cancellationToken);
        }
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
