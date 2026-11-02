using System;
using System.Linq;
using Sufni.App.MapsAndTracks.Services;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
namespace Sufni.App.Sessions.Coordination;

/// <summary>Owns session metadata, processing options, GPS alignment and deletion.</summary>
internal sealed class SessionCoordinator : ISessionCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SessionCoordinator>();

    private readonly IUiThreadDispatcher dispatcher;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly ISynchronizableRepository<Track> trackRepository;
    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly ISessionRecomputeEngine recomputeEngine;
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions;
    private readonly StorePublication publication;
    private readonly ITrackStoreWriter trackStore;

    public event EventHandler<SessionRecomputeAllOutcome>? RecomputeAllFinished;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        ISessionRecomputeEngine recomputeEngine,
        ISessionPersistenceTransactionRunner sessionPersistenceTransactions,
        IUiThreadDispatcher dispatcher,
        ISessionProcessedTelemetryReader processedTelemetryReader,
        ISynchronizableRepository<Track> trackRepository,
        StorePublication publication,
        ITrackStoreWriter trackStore)
    {
        this.dispatcher = dispatcher;
        this.publication = publication;
        this.trackStore = trackStore;
        this.processedTelemetryReader = processedTelemetryReader;
        this.trackRepository = trackRepository;
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.sourceStore = sourceStore;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.recomputeEngine = recomputeEngine;
        this.sessionPersistenceTransactions = sessionPersistenceTransactions;
    }

    public async Task<SessionStalenessReconciliation> ReconcileStalenessAsync(
        RecordedSessionDomainSnapshot domain, SessionRecomputeChoice choice = SessionRecomputeChoice.Inspect,
        IProgress<SessionRecomputeAllProgress>? progress = null)
    {
        if (recomputeEngine.IsActive(domain.Session.Id)) return new SessionStalenessReconciliation.Active();
        if (domain.Staleness is SessionStaleness.MissingRawSource)
            return new SessionStalenessReconciliation.Unavailable(domain.Staleness);
        if (!domain.Staleness.IsStale) return new SessionStalenessReconciliation.Current();
        if (!domain.Staleness.CanRecompute) return new SessionStalenessReconciliation.Unavailable(domain.Staleness);
        if (choice == SessionRecomputeChoice.Inspect)
            return new SessionStalenessReconciliation.Required(domain.CurrentFingerprint, domain.Session.ProcessingFingerprintJson);
        if (choice == SessionRecomputeChoice.ThisSession)
            return new SessionStalenessReconciliation.Recomputed(await recomputeEngine.RequestRecomputeAsync(domain.Session.Id));

        SessionRecomputeAllOutcome outcome;
        try
        {
            outcome = new SessionRecomputeAllOutcome.Completed(await recomputeEngine.RequestRecomputeAllAsync(progress));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Session bulk recompute failed");
            outcome = new SessionRecomputeAllOutcome.Failed(SessionRecomputeFailureReason.RecomputeAll, exception.Message);
        }
        await dispatcher.InvokeAsync(() => RecomputeAllFinished?.Invoke(this, outcome));
        return new SessionStalenessReconciliation.Bulk(outcome);
    }

    public async Task<SessionRecomputeResult> SetProcessingOptionAsync(Guid sessionId, int windowMilliseconds)
    {
        var window = new TelemetryProcessingOptions(windowMilliseconds).ClampedVelocityFilterWindowMilliseconds;
        try
        {
            var previous = await backgroundTaskRunner.RunAsync(() => sessionRepository.GetSessionAsync(sessionId));
            if (previous is null) return new SessionRecomputeResult.Missing();
            if (SessionSnapshot.From(previous).ProcessingOptions.ClampedVelocityFilterWindowMilliseconds == window)
                return await recomputeEngine.RequestRecomputeAsync(sessionId);

            var saved = await backgroundTaskRunner.RunAsync(() => sessionRepository.SetProcessingOptionAsync(sessionId, window));
            if (saved is null) return new SessionRecomputeResult.Missing();
            Task<SessionRecomputeResult> recompute = null!;
            await dispatcher.InvokeAsync(async () =>
            {
                await sessionStore.PublishSessionsChangedAsync([SessionSnapshot.From(saved)]);
                // Mark the request active before queued projection work can prompt.
                recompute = recomputeEngine.RequestRecomputeAsync(sessionId);
            });
            var result = await recompute;
            if (result is SessionRecomputeResult.Failed or SessionRecomputeResult.ProcessingFailed or
                SessionRecomputeResult.NotRecomputable or SessionRecomputeResult.Missing)
            {
                var restored = await backgroundTaskRunner.RunAsync(() => sessionRepository.RestoreProcessingOptionAsync(
                    sessionId, window, previous.VelocityFilterWindowMilliseconds));
                if (restored is not null)
                    await sessionStore.PublishSessionsChangedAsync([SessionSnapshot.From(restored)]);
            }
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Processing option change failed for {SessionId}", sessionId);
            return new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.ProcessingOption, exception.Message);
        }
    }

    public async Task<SessionPreferenceLoadResult> LoadPreferencesAsync(Guid sessionId)
    {
        try
        {
            return new SessionPreferenceLoadResult.Loaded(await sessionPreferences.GetRecordedAsync(sessionId));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Session preferences load failed for {SessionId}", sessionId);
            return new SessionPreferenceLoadResult.Failed(SessionEditorFailureReason.LoadPreferences, exception.Message);
        }
    }

    public async Task<SessionPreferenceSaveResult> SavePreferencesAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update)
    {
        try
        {
            await sessionPreferences.UpdateRecordedAsync(sessionId, update);
            return new SessionPreferenceSaveResult.Saved();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Session preferences save failed for {SessionId}", sessionId);
            return new SessionPreferenceSaveResult.Failed(SessionEditorFailureReason.SavePreferences, exception.Message);
        }
    }

    public async Task<GpsOffsetResult> ShiftGpsOffsetAsync(
        Guid sessionId,
        double deltaSeconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await backgroundTaskRunner.RunAsync(async () =>
            {
                var current = await sessionRepository.GetSessionAsync(sessionId);
                if (current is null) return (GpsOffsetResult)new GpsOffsetResult.SessionMissing();
                var telemetry = await processedTelemetryReader.GetAsync(sessionId, cancellationToken);
                if (telemetry is null) return new GpsOffsetResult.TelemetryUnavailable();

                var offset = SessionTrackTimeline.NormalizeGpsOffsetSeconds(current.GpsOffsetSeconds + deltaSeconds);
                var timestamp = current.Timestamp ?? telemetry.Metadata.Timestamp;
                var duration = current.DurationSeconds ?? telemetry.Metadata.Duration;
                var selected = SessionTrackSelection.Select(trackStore.GetAll(), timestamp, duration, offset, current.WindowStartSeconds);
                if (selected is null || await trackRepository.GetAsync(selected.Id) is not { } track)
                    return new GpsOffsetResult.NoMatchingTrack();
                var points = SessionTrackProjection.GenerateSessionTrack(track, timestamp, duration, offset, current.WindowStartSeconds);
                if (points is not { Count: > 0 }) return new GpsOffsetResult.NoMatchingTrack();
                cancellationToken.ThrowIfCancellationRequested();
                await sessionRepository.UpdateGpsAlignmentAsync(sessionId, offset);
                var fresh = await sessionRepository.GetSessionAsync(sessionId);
                if (fresh is null)
                {
                    await sessionStore.PublishSessionsRemovedAsync([sessionId]);
                    return new GpsOffsetResult.SessionMissing();
                }
                await sessionStore.PublishSessionsChangedAsync([SessionSnapshot.From(fresh)]);
                return new GpsOffsetResult.Applied();
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error(exception, "GPS alignment failed for {SessionId}", sessionId);
            return new GpsOffsetResult.Failed(GpsOffsetFailureReason.Save, exception.Message);
        }
    }

    public async Task<SessionSaveResult> SaveAsync(SessionDraft draft, SessionDraft draftBase)
    {
        logger.Information("Starting session save for {SessionId}", draft.Id);
        var current = sessionStore.Get(draft.Id);
        if (current is null) return new SessionSaveResult.Missing();
        if (SessionDraft.From(current) != draftBase)
        {
            logger.Warning("Session save conflict for {SessionId}", draft.Id);
            return new SessionSaveResult.Conflict(current);
        }
        var session = draft.ApplyTo(current);

        try
        {
            var fresh = await backgroundTaskRunner.RunAsync(async () =>
            {
                await sessionRepository.PutSessionAsync(session);
                return await sessionRepository.GetSessionAsync(session.Id);
            });
            if (fresh is null)
            {
                await sessionStore.PublishSessionsRemovedAsync([session.Id]);
                return new SessionSaveResult.Missing();
            }

            var saved = SessionSnapshot.From(fresh);
            await sessionStore.PublishSessionsChangedAsync([saved]);
            logger.Information("Session save completed for {SessionId}", session.Id);
            return new SessionSaveResult.Saved(saved);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session save failed for {SessionId}", session.Id);
            return new SessionSaveResult.Failed(SessionSaveFailureReason.Save, e.Message);
        }
    }

    public async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
    {
        logger.Information("Starting session delete for {SessionId}", sessionId);

        try
        {
            var commit = await sessionPersistenceTransactions.DeleteSessionAsync(sessionId);
            try
            {
                await sessionPreferences.RemoveRecordedAsync(sessionId);
            }
            catch (Exception e)
            {
                logger.Warning(e, "Failed to remove recorded-session preferences after deleting session {SessionId}", sessionId);
            }

            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                if (commit.SourceDeleted) await sourceStore.PublishSourcesRemovedAsync([sessionId]);
                if (commit.RemovedTrackId is { } trackId) await trackStore.PublishTracksRemovedAsync([trackId]);
                await sessionStore.PublishSessionsRemovedAsync([sessionId]);
            });
            logger.Information("Session delete completed for {SessionId}", sessionId);
            return new SessionDeleteResult.Deleted();
        }
        catch (Exception e)
        {
            logger.Error(e, "Session delete failed for {SessionId}", sessionId);
            return new SessionDeleteResult.Failed(SessionDeleteFailureReason.Delete, e.Message);
        }
    }

}

public abstract record SessionSaveResult
{
    private SessionSaveResult() { }

    public sealed record Saved(SessionSnapshot Snapshot) : SessionSaveResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionSaveResult;
    public sealed record Missing : SessionSaveResult;
    public sealed record Failed(SessionSaveFailureReason Reason, string? Details = null) : SessionSaveResult;
}

/// <summary>
/// Result of attempting to rebuild a recorded session's derived telemetry.
/// It distinguishes successful recompute, displacement by a newer explicit
/// request, unrecomputable current state, and failure.
/// </summary>
public abstract record SessionRecomputeResult
{
    private SessionRecomputeResult() { }

    public sealed record Recomputed(long NewBaselineUpdated) : SessionRecomputeResult;

    /// <summary>
    /// A newer explicit recompute request displaced this run before it
    /// committed. It is terminal-no-op for the caller: the newer run owns the result,
    /// and the caller never advances a baseline or clears dirty from it.
    /// </summary>
    public sealed record Superseded : SessionRecomputeResult;
    public sealed record NotRecomputable(SessionStaleness Reason) : SessionRecomputeResult;
    public sealed record Missing : SessionRecomputeResult;
    public sealed record Failed(SessionRecomputeFailureReason Reason, string? Details = null) : SessionRecomputeResult;
    public sealed record ProcessingFailed(SessionProcessingFailureReason Reason, string? Details = null) : SessionRecomputeResult;
}

/// <summary>
/// Aggregate outcome of a recompute-all request. The counts are disjoint and
/// sum to <see cref="Total"/>, the number of live sessions considered.
/// </summary>
public sealed record SessionRecomputeAllResult(
    int Total,
    int Recomputed,
    int Superseded,
    int NotRecomputable,
    int Failed);

/// <summary>
/// Incremental progress for a recompute-all run: how many of the considered
/// sessions have finished so far.
/// </summary>
public sealed record SessionRecomputeAllProgress(int Completed, int Total);

public abstract record SessionDeleteResult
{
    private SessionDeleteResult() { }
    public sealed record Deleted : SessionDeleteResult;
    public sealed record Failed(SessionDeleteFailureReason Reason, string? Details = null) : SessionDeleteResult;
}

public abstract record GpsOffsetResult
{
    private GpsOffsetResult() { }
    public sealed record Applied : GpsOffsetResult;
    public sealed record NoMatchingTrack : GpsOffsetResult;
    public sealed record TelemetryUnavailable : GpsOffsetResult;
    public sealed record SessionMissing : GpsOffsetResult;
    public sealed record Failed(GpsOffsetFailureReason Reason, string? Details = null) : GpsOffsetResult;
}
