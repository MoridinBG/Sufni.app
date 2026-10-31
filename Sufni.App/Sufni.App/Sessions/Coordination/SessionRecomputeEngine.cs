using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

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
    Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds non-current sessions in parallel. Missing or unavailable sessions
    /// are skipped; displaced work stops with Superseded. Fingerprints are the
    /// checkpoint: no run resumes after restart, and staleness is derived again.
    /// Progress counts only the selected non-current sessions.
    /// </summary>
    Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        IProgress<SessionRecomputeAllProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True from the moment a request is accepted (synchronously, before any
    /// await) until that session's run map empties. The staleness prompter
    /// reads it to avoid prompting for a recompute the user just triggered.
    /// </summary>
    bool IsActive(Guid sessionId);
    Task StopAsync();
}

public sealed class SessionRecomputeEngine : ISessionRecomputeEngine, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<SessionRecomputeEngine>();
    private static readonly int RecomputeAllMaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ITrackRepository trackQueries;
    private readonly IProcessingFingerprintService fingerprintService;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionSourceRepository sourceRepository;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;

    // Mirrors RecordedSessionProjection's stateGate pattern: a single lock guards the
    // run map and the monotonic sequence; there is deliberately no per-id
    // SemaphoreSlim. The map is the source of truth for both currency (which run
    // may commit) and IsActive.
    private readonly System.Threading.Lock stateGate = new();
    private readonly Dictionary<Guid, Run> runs = new();
    private long sequence;
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<Task> activeTasks = [];

    public SessionRecomputeEngine(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ITrackRepository trackQueries,
        ISynchronizableRepository<Track> trackEntityRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionDomainQuery recordedSessionDomainQuery,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        IRecordedSessionSourceRepository sourceRepository,
        IProcessingFingerprintService fingerprintService)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.trackQueries = trackQueries;
        this.fingerprintService = fingerprintService;
        this.trackEntityRepository = trackEntityRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sourceStore = sourceStore;
        this.sourceRepository = sourceRepository;
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

    public Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource cts;
        long seq;
        var tcs = new TaskCompletionSource<SessionRecomputeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (stateGate)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            // Cancel-and-replace: the previous run for this id (if any) loses its
            // commit at guard (i); install this run as the current one. IsActive is
            // already true here — before any await — because the entry now exists.
            if (runs.TryGetValue(sessionId, out var oldRun))
            {
                oldRun.Cts.Cancel();
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            seq = ++sequence;
            runs[sessionId] = new Run(cts, seq, tcs.Task);
            activeTasks.Add(tcs.Task);
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
                var result = await backgroundTaskRunner.RunAsync(
                    () => RecomputeAsync(sessionId, seq, cts.Token), cts.Token).ConfigureAwait(false);
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
                tcs.SetResult(new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.Recompute, e.Message));
            }
            finally
            {
                lock (stateGate)
                {
                    activeTasks.Remove(tcs.Task);
                    if (runs.TryGetValue(sessionId, out var current) && current.Seq == seq)
                    {
                        runs.Remove(sessionId);
                    }
                }

                cts.Dispose();
            }
        }
    }

    public Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        IProgress<SessionRecomputeAllProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        lock (stateGate)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
            var task = backgroundTaskRunner.RunAsync(
                () => RecomputeAllAsync(progress, cancellation.Token), cancellation.Token);
            activeTasks.Add(task);
            _ = ReleaseAsync(task, cancellation);
            return task;
        }
    }

    private async Task ReleaseAsync(Task task, CancellationTokenSource cancellation)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (stateGate) activeTasks.Remove(task);
        cancellation.Dispose();
    }

    public async Task StopAsync()
    {
        await shutdown.CancelAsync();
        Task[] tasks;
        lock (stateGate) tasks = activeTasks.ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    public void Dispose() => shutdown.Dispose();

    private async Task<SessionRecomputeAllResult> RecomputeAllAsync(
        IProgress<SessionRecomputeAllProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = (await sessionRepository.GetActiveSessionIdsAsync())
            .Where(id => recordedSessionDomainQuery.Get(id)?.Staleness is not SessionStaleness.Current)
            .ToList();

        logger.Information("Starting recompute-all for {SessionCount} sessions", ids.Count);

        var recomputed = 0;
        var superseded = 0;
        var notRecomputable = 0;
        var failed = 0;
        var completed = 0;

        progress?.Report(new SessionRecomputeAllProgress(0, ids.Count));

        var options = new ParallelOptions { MaxDegreeOfParallelism = RecomputeAllMaxDegreeOfParallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(ids, options, async (id, token) =>
        {
            var result = await RequestRecomputeAsync(id, token);
            switch (result)
            {
                case SessionRecomputeResult.Recomputed:
                    Interlocked.Increment(ref recomputed);
                    break;
                case SessionRecomputeResult.Superseded:
                    Interlocked.Increment(ref superseded);
                    break;
                case SessionRecomputeResult.Missing:
                case SessionRecomputeResult.NotRecomputable:
                    Interlocked.Increment(ref notRecomputable);
                    break;
                case SessionRecomputeResult.ProcessingFailed:
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
        long seq,
        CancellationToken cancellationToken)
    {
        logger.Information("Starting recorded session recompute for {SessionId}", sessionId);

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
                return new SessionRecomputeResult.Missing();
            }

            if (!domain.Staleness.CanManualRecompute)
            {
                logger.Warning("Recorded session {SessionId} is not recomputable because {Reason}", sessionId, domain.Staleness.GetType().Name);
                return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
            }

            var sourceSessionId = domain.Session.RecordingSourceSessionId;
            cancellationToken.ThrowIfCancellationRequested();
            var source = await sourceRepository.GetRecordedSessionSourceAsync(sourceSessionId);
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
                await sourceStore.PublishSourcesChangedAsync([loadedSourceSnapshot], cancellationToken);
                domain = recordedSessionDomainQuery.Get(sessionId);
                if (domain is null)
                {
                    return new SessionRecomputeResult.Missing();
                }

                if (!domain.Staleness.CanManualRecompute)
                {
                    logger.Warning("Recorded session {SessionId} is not recomputable after source refresh because {Reason}", sessionId, domain.Staleness.GetType().Name);
                    return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
                }

                var refreshedSourceSessionId = domain.Session.RecordingSourceSessionId;
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

            var reprocessResult = await recordedSessionReprocessor.ReprocessAsync(
                RecordedSessionDerivationInput.Create(domain.Session, domain.Setup!, domain.Bike!, source), cancellationToken);

            // Synchronous processing cannot be interrupted; cancellation must still
            // prevent the completed generation from reaching persistence.
            cancellationToken.ThrowIfCancellationRequested();

            if (reprocessResult is RecordedSessionReprocessResult.Failed failure)
                return new SessionRecomputeResult.ProcessingFailed(failure.Reason, failure.Details);
            var processed = (RecordedSessionReprocessResult.Processed)reprocessResult;

            var persisted = await sessionRepository.GetSessionAsync(sessionId);
            if (persisted is null)
            {
                return new SessionRecomputeResult.Missing();
            }

            var expectedInputs = await sessionRepository.GetProcessingInputBundleAsync(sessionId);
            if (expectedInputs is null || fingerprintService.CreateCurrentDatabaseInputs(expectedInputs) != processed.Fingerprint)
                continue;

            var previousFullTrackId = persisted.FullTrack;
            var previousFullTrack = previousFullTrackId.HasValue
                ? await trackEntityRepository.GetAsync(previousFullTrackId.Value)
                : null;
            var generation = processed.Generation;
            var generatedTrack = generation.TrackPoints is { Count: > 0 } points
                ? new Track(points) : null;
            var newFullTrack = generatedTrack is not null &&
                               (previousFullTrack is null || previousFullTrack.Id != generatedTrack.Id)
                ? generatedTrack : null;
            var fullTrackId = newFullTrack?.Id ?? previousFullTrackId;
            if (newFullTrack is null)
            {
                if (fullTrackId is null && persisted.Timestamp is { } timestamp)
                    fullTrackId = await trackQueries.FindTrackContainingTimestampAsync(timestamp);
                var track = previousFullTrack ?? (fullTrackId is { } trackId
                    ? await trackEntityRepository.GetAsync(trackId) : null);
                if (track is not null) generation = generation.WithTrackWindow(track, SessionSnapshot.From(persisted));
            }
            var prepared = generation.CreateSession(SessionSnapshot.From(persisted), fullTrackId);

            // A newer explicit request owns the result; the transaction separately
            // checks all persisted inputs, including the processing option.
            if (cancellationToken.IsCancellationRequested || !IsCurrent(sessionId, seq))
            {
                return new SessionRecomputeResult.Superseded();
            }

            // The transaction rejects inputs changed after the fingerprint check;
            // a passive change retries from the current domain snapshots.
            var fresh = await sessionRepository.UpdateProcessedDerivedDataAsync(
                prepared, newFullTrack, expectedInputs);
            if (fresh is null)
            {
                logger.Information("Recorded session recompute for {SessionId} re-enqueued after a passive input change", sessionId);
                continue;
            }

            var snapshot = SessionSnapshot.From(fresh);
            await sessionStore.PublishSessionsChangedAsync([snapshot]);

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
