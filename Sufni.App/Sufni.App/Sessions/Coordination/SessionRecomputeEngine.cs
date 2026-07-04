using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

/// <summary>
/// Why a recompute was requested. Logging/telemetry only — it does not change
/// engine behavior.
/// </summary>
public enum RecomputeReason
{
    ProcessingPreferenceChanged,
    StaleOnOpen,
    DependencyChanged,
    ManualFromList,
    GpsOffsetChanged,
    SourceWindowChanged,
    Migration,
    RecomputeAll
}

/// <summary>
/// Serialized, per-session cancel-and-replace recompute engine and the single
/// owner of recompute liveness. A newer request for the same session
/// cancels and replaces the in-flight one; a run whose DB inputs change underneath
/// it re-enqueues itself until it converges. Callers never see
/// <see cref="OperationCanceledException"/>: a run displaced by a newer explicit
/// request resolves to <see cref="SessionRecomputeResult.Superseded"/>.
/// </summary>
public interface ISessionRecomputeEngine
{
    Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason);

    /// <summary>
    /// Rebuilds every live recorded session, fanning the per-session requests out
    /// with a degree of parallelism scaled to the available CPU cores. Each
    /// session goes through <see cref="RequestRecomputeAsync"/>, so per-session
    /// cancel-and-replace and the not-recomputable guard still apply; sessions
    /// that cannot be recomputed are counted and skipped rather than surfaced as
    /// failures. When supplied, <paramref name="progress"/> is reported after each
    /// session finishes so a caller can drive a progress indicator.
    /// </summary>
    Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        RecomputeReason reason,
        IProgress<SessionRecomputeAllProgress>? progress = null);

    /// <summary>
    /// True from the moment a request is accepted (synchronously, before any
    /// await) until that session's run map empties. The staleness prompter
    /// reads it to avoid prompting for a recompute the user just triggered.
    /// </summary>
    bool IsActive(Guid sessionId);
}

public sealed class SessionRecomputeEngine : ISessionRecomputeEngine
{
    private static readonly ILogger logger = Log.ForContext<SessionRecomputeEngine>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IRecordedSessionProcessingOptionCache processingOptionCache;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;

    // Mirrors RecordedSessionProjection's stateGate pattern: a single lock guards the
    // run map and the monotonic sequence; there is deliberately no per-id
    // SemaphoreSlim. The map is the source of truth for both currency (which run
    // may commit) and IsActive.
    private readonly System.Threading.Lock stateGate = new();
    private readonly Dictionary<Guid, Run> runs = new();
    private long sequence;

    public SessionRecomputeEngine(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        ISynchronizableRepository<Track> trackEntityRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        IRecordedSessionProcessingOptionCache processingOptionCache,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionDomainQuery recordedSessionDomainQuery,
        IRecordedSessionReprocessor recordedSessionReprocessor)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.trackEntityRepository = trackEntityRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.processingOptionCache = processingOptionCache;
        this.sourceStore = sourceStore;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
    }

    private sealed record Run(CancellationTokenSource Cts, long Seq, Task<SessionRecomputeResult> Task);

    public bool IsActive(Guid sessionId)
    {
        lock (stateGate)
        {
            return runs.ContainsKey(sessionId);
        }
    }

    public Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason)
    {
        CancellationTokenSource cts;
        long seq;
        var tcs = new TaskCompletionSource<SessionRecomputeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (stateGate)
        {
            // Cancel-and-replace: the previous run for this id (if any) loses its
            // commit at guard (i); install this run as the current one. IsActive is
            // already true here — before any await — because the entry now exists.
            if (runs.TryGetValue(sessionId, out var oldRun))
            {
                oldRun.Cts.Cancel();
            }

            cts = new CancellationTokenSource();
            seq = ++sequence;
            runs[sessionId] = new Run(cts, seq, tcs.Task);
        }

        // Run the body off the lock; the TaskCompletionSource is what the map holds
        // so a superseding request can await this run without the lock being held
        // across the body's work.
        _ = DriveAsync();
        return tcs.Task;

        async Task DriveAsync()
        {
            try
            {
                // Start this run immediately. The displaced run may still be inside
                // the synchronous telemetry pipeline, but it is no longer current
                // and will no-op at guard (i) before persistence.
                var result = await RecomputeAsync(sessionId, reason, seq, cts.Token).ConfigureAwait(false);
                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                // A newer explicit request cancelled this run mid-flight. Map to
                // Superseded rather than letting OCE escape to VM/list callers.
                tcs.SetResult(new SessionRecomputeResult.Superseded());
            }
            catch (Exception e)
            {
                logger.Error(e, "Recorded session recompute failed for {SessionId}", sessionId);
                tcs.SetResult(new SessionRecomputeResult.Failed(e.Message));
            }
            finally
            {
                lock (stateGate)
                {
                    if (runs.TryGetValue(sessionId, out var current) && current.Seq == seq)
                    {
                        runs.Remove(sessionId);
                    }
                }

                cts.Dispose();
            }
        }
    }

    public async Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        RecomputeReason reason,
        IProgress<SessionRecomputeAllProgress>? progress = null)
    {
        await processingOptionCache.HydrateAsync();
        var ids = await sessionRepository.GetActiveSessionIdsAsync();

        logger.Information("Starting recompute-all for {SessionCount} sessions ({Reason})", ids.Count, reason);

        var recomputed = 0;
        var superseded = 0;
        var notRecomputable = 0;
        var failed = 0;
        var completed = 0;

        progress?.Report(new SessionRecomputeAllProgress(0, ids.Count));

        // Scale the fan-out to the available hardware. Each RequestRecomputeAsync
        // offloads its heavy reprocessing to the background task runner, so
        // bounding concurrency to the processor count keeps the cores busy
        // without oversubscribing them or flooding the shared DB connection.
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        await Parallel.ForEachAsync(ids, options, async (id, _) =>
        {
            var result = await RequestRecomputeAsync(id, reason);
            switch (result)
            {
                case SessionRecomputeResult.Recomputed:
                    Interlocked.Increment(ref recomputed);
                    break;
                case SessionRecomputeResult.Superseded:
                    Interlocked.Increment(ref superseded);
                    break;
                case SessionRecomputeResult.NotRecomputable:
                    Interlocked.Increment(ref notRecomputable);
                    break;
                case SessionRecomputeResult.Failed:
                    Interlocked.Increment(ref failed);
                    break;
            }

            progress?.Report(new SessionRecomputeAllProgress(Interlocked.Increment(ref completed), ids.Count));
        });

        logger.Information(
            "Recompute-all completed: {Recomputed} recomputed, {NotRecomputable} skipped, {Failed} failed, {Superseded} superseded",
            recomputed,
            notRecomputable,
            failed,
            superseded);

        return new SessionRecomputeAllResult(ids.Count, recomputed, superseded, notRecomputable, failed);
    }

    private async Task<SessionRecomputeResult> RecomputeAsync(
        Guid sessionId,
        RecomputeReason reason,
        long seq,
        CancellationToken cancellationToken)
    {
        logger.Information("Starting recorded session recompute for {SessionId} ({Reason})", sessionId, reason);

        // The loop is the engine's liveness self-heal: when the write
        // transaction rolls back on a passive setup/bike/source change, re-read all
        // inputs and recompute rather than surfacing a neutral result. A newer
        // explicit request breaks the loop through the shared token (guard (i) /
        // the ThrowIfCancellationRequested checks).
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var domain = recordedSessionDomainQuery.Get(sessionId);
            if (domain is null)
            {
                logger.Warning("Recorded session recompute failed because session {SessionId} is missing", sessionId);
                return new SessionRecomputeResult.Failed("Session is missing.");
            }

            if (!domain.Staleness.CanManualRecompute)
            {
                logger.Warning("Recorded session {SessionId} is not recomputable because {Reason}", sessionId, domain.Staleness.GetType().Name);
                return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
            }

            var sourceSessionId = RecordedSessionDerivationResolver.GetEffectiveSourceSessionId(
                sessionId,
                domain.DerivationWindow);
            var source = await sourceStore.LoadAsync(sourceSessionId, cancellationToken);
            if (source is null)
            {
                logger.Warning(
                    "Recorded session recompute failed because source {SourceSessionId} for session {SessionId} is missing",
                    sourceSessionId,
                    sessionId);
                return new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource());
            }

            // Reconcile a source row that changed on disk since the projection last read
            // it, then re-read the domain so the reprocess and its fingerprint see
            // the current source (baseline-conflict checks of the old code dropped).
            var loadedSourceSnapshot = RecordedSessionSourceSnapshot.From(source);
            if (domain.Source != loadedSourceSnapshot)
            {
                await sourceStore.PublishSourcesChangedAsync([source.SessionId], cancellationToken);
                domain = recordedSessionDomainQuery.Get(sessionId);
                if (domain is null)
                {
                    logger.Warning("Recorded session recompute failed because session {SessionId} disappeared after source refresh", sessionId);
                    return new SessionRecomputeResult.Failed("Session is missing.");
                }

                if (!domain.Staleness.CanManualRecompute)
                {
                    logger.Warning("Recorded session {SessionId} is not recomputable after source refresh because {Reason}", sessionId, domain.Staleness.GetType().Name);
                    return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
                }

                var refreshedSourceSessionId = RecordedSessionDerivationResolver.GetEffectiveSourceSessionId(
                    sessionId,
                    domain.DerivationWindow);
                if (source.SessionId != refreshedSourceSessionId)
                {
                    logger.Information(
                        "Recorded session recompute for {SessionId} re-enqueued after source window target changed from {LoadedSourceSessionId} to {CurrentSourceSessionId}",
                        sessionId,
                        source.SessionId,
                        refreshedSourceSessionId);
                    continue;
                }
            }

            // Read the processing option at run time so cancel-and-replace yields
            // the last cached value. The reprocessor's fingerprint is exactly
            // CreateCurrent(domain, option) — i.e. F_in, the input-coherence key;
            // no separate signature is computed.
            var processingOptions = processingOptionCache.Get(sessionId);

            var reprocessResult = await backgroundTaskRunner.RunAsync(
                () => recordedSessionReprocessor.ReprocessAsync(domain, source, processingOptions, cancellationToken),
                cancellationToken);

            // A superseding request cannot interrupt the synchronous pipeline above
            // (BackgroundTaskRunner.RunAsync is Task.Run(work, ct); ReprocessAsync
            // checks the token once at entry then runs token-less
            // TelemetryData.FromRecording). Cancellation bounds correctness, not
            // CPU/latency: re-check it before doing any persistence work.
            cancellationToken.ThrowIfCancellationRequested();

            var persisted = await sessionRepository.GetSessionAsync(sessionId);
            if (persisted is null)
            {
                logger.Warning("Recorded session recompute failed because session {SessionId} disappeared before persistence", sessionId);
                return new SessionRecomputeResult.Failed("Session is missing.");
            }

            var previousFullTrackId = persisted.FullTrack;
            var previousFullTrack = previousFullTrackId.HasValue
                ? await trackEntityRepository.GetAsync(previousFullTrackId.Value)
                : null;
            Track? newFullTrack = null;

            if (reprocessResult.GeneratedFullTrack is null)
            {
                persisted.FullTrack = previousFullTrackId;
                persisted.Track = null;
            }
            else if (previousFullTrack is not null &&
                     TrackContentHash.PointsEqual(previousFullTrack, reprocessResult.GeneratedFullTrack))
            {
                persisted.FullTrack = previousFullTrackId;
            }
            else
            {
                newFullTrack = reprocessResult.GeneratedFullTrack;
                persisted.Track = null;
            }

            // Commit guard (i): a newer explicit request supersedes this run. The
            // option lives in ISessionPreferences (not a DB column), so this
            // still-current / cancellation check — not the transaction — is what
            // guards an option change. Abort without writing; the newer run
            // owns the result.
            if (cancellationToken.IsCancellationRequested || !IsCurrent(sessionId, seq))
            {
                return new SessionRecomputeResult.Superseded();
            }

            // Commit guard (ii): UpdateProcessedDerivedDataAsync re-checks the
            // DB-input part of F_in inside the write transaction and returns null on
            // a passive setup/bike/source change. Re-enqueue by looping
            // with freshly read inputs rather than surfacing a neutral result.
            var fresh = await sessionTelemetryWriter.UpdateProcessedDerivedDataAsync(
                persisted,
                reprocessResult.ProcessedTelemetry,
                newFullTrack,
                reprocessResult.Fingerprint);
            if (fresh is null)
            {
                logger.Information("Recorded session recompute for {SessionId} re-enqueued after a passive input change", sessionId);
                continue;
            }

            var snapshot = SessionSnapshot.From(fresh);
            await sessionStore.PublishSessionsChangedAsync([snapshot.Id], cancellationToken);

            await DeletePreviousFullTrackIfOrphanedAsync(previousFullTrackId, fresh.FullTrack, sessionId);

            logger.Information("Recorded session recompute completed for {SessionId}", sessionId);
            return new SessionRecomputeResult.Recomputed(snapshot.Updated);
        }
    }

    private bool IsCurrent(Guid sessionId, long seq)
    {
        lock (stateGate)
        {
            return runs.TryGetValue(sessionId, out var current) && current.Seq == seq;
        }
    }

    private async Task DeletePreviousFullTrackIfOrphanedAsync(Guid? previousFullTrackId, Guid? currentFullTrackId, Guid sessionId)
    {
        if (!previousFullTrackId.HasValue || previousFullTrackId == currentFullTrackId)
        {
            return;
        }

        var stillReferenced = await sessionRepository.HasOtherActiveSessionWithFullTrackAsync(
            previousFullTrackId.Value,
            sessionId);
        if (stillReferenced)
        {
            return;
        }

        try
        {
            await trackEntityRepository.DeleteAsync(previousFullTrackId.Value);
        }
        catch (Exception e)
        {
            logger.Warning(e, "Failed to delete orphaned track {TrackId} after recomputing session {SessionId}", previousFullTrackId.Value, sessionId);
        }
    }
}
