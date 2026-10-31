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

/// <summary>Owns session metadata, processing options, GPS alignment, live saves and deletion.</summary>
internal sealed class SessionCoordinator : ISessionCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SessionCoordinator>();

    private readonly IUiThreadDispatcher dispatcher;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly ISynchronizableRepository<Track> trackRepository;
    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ITrackRepository trackQueries;
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly ISynchronizableRepository<Bike> bikeRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly ISessionRecomputeEngine recomputeEngine;
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions;
    private readonly StorePublication publication;
    private readonly ITrackStoreWriter trackStore;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ITrackRepository trackQueries,
        ISynchronizableRepository<Setup> setupRepository,
        ISynchronizableRepository<Bike> bikeRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionReprocessor recordedSessionReprocessor,
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
        this.trackQueries = trackQueries;
        this.setupRepository = setupRepository;
        this.bikeRepository = bikeRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.recomputeEngine = recomputeEngine;
        this.sessionPersistenceTransactions = sessionPersistenceTransactions;
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
                if (telemetry is null) return new GpsOffsetResult.NoMatchingTrack();

                var offset = SessionTrackTimeline.NormalizeGpsOffsetSeconds(current.GpsOffsetSeconds + deltaSeconds);
                var timestamp = current.Timestamp ?? telemetry.Metadata.Timestamp;
                var duration = current.DurationSeconds ?? telemetry.Metadata.Duration;
                var selected = SessionTrackSelection.Select(trackStore.GetAll(), timestamp, duration, offset);
                if (selected is null || await trackRepository.GetAsync(selected.Id) is not { } track)
                    return new GpsOffsetResult.NoMatchingTrack();
                var points = SessionTrackProjection.GenerateSessionTrack(track, timestamp, duration, offset);
                if (points is not { Count: > 0 }) return new GpsOffsetResult.NoMatchingTrack();
                var metrics = SessionSummaryMetricsCalculator.Calculate(current.DurationSeconds ?? telemetry.Metadata.Duration, points);
                cancellationToken.ThrowIfCancellationRequested();
                await sessionRepository.UpdateGpsAlignmentAsync(sessionId, metrics, offset);
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

    public async Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated)
    {
        logger.Information("Starting session save for {SessionId}", session.Id);

        // Optimistic concurrency is the in-memory store-snapshot compare, not a
        // SQL WHERE updated=? guard: the store's last-seen Updated is the editor's
        // baseline, so a newer store snapshot means an external edit landed first.
        var current = sessionStore.Get(session.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            logger.Warning("Session save conflict for {SessionId}", session.Id);
            return new SessionSaveResult.Conflict(current);
        }

        if (current is not null) session.VelocityFilterWindowMilliseconds = current.VelocityFilterWindowMilliseconds;

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
            return new SessionSaveResult.Saved(saved.Id, saved.Updated);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session save failed for {SessionId}", session.Id);
            return new SessionSaveResult.Failed(SessionSaveFailureReason.Save, e.Message);
        }
    }

    public async Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        logger.Information("Starting live session save for {SessionId}", session.Id);

        try
        {
            return await backgroundTaskRunner.RunAsync<LiveSessionSaveResult>(async () =>
            {
                var source = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture);
                var setup = await setupRepository.GetAsync(capture.Context.SetupId)
                            ?? throw new InvalidOperationException("Setup is missing.");
                var bike = await bikeRepository.GetAsync(setup.BikeId)
                           ?? throw new InvalidOperationException("Bike is missing.");
                var setupSnapshot = SetupSnapshot.From(setup, boardId: null);
                var bikeSnapshot = BikeSnapshot.From(bike);
                var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
                var sessionSnapshot = SessionSnapshot.From(session) with { Id = source.SessionId };
                var input = new RecordedSessionDerivationInput(
                    sessionSnapshot, setupSnapshot, bikeSnapshot, sourceSnapshot, source.Payload);

                var reprocessResult = await recordedSessionReprocessor.ReprocessAsync(input, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (reprocessResult is RecordedSessionReprocessResult.Failed failure)
                    return new LiveSessionSaveResult.ProcessingFailed(failure.Reason, failure.Details);
                var processed = (RecordedSessionReprocessResult.Processed)reprocessResult;
                var generation = processed.Generation;
                var newTrack = generation.TrackPoints is { Count: > 0 } points
                    ? new Track(points) : null;
                var fullTrackId = newTrack?.Id ?? session.FullTrack;
                if (newTrack is null)
                {
                    if (fullTrackId is null && session.Timestamp is { } timestamp)
                        fullTrackId = await trackQueries.FindTrackContainingTimestampAsync(timestamp);
                    if (fullTrackId is { } trackId && await trackRepository.GetAsync(trackId) is { } track)
                        generation = generation.WithTrackWindow(track, sessionSnapshot);
                }
                var prepared = generation.CreateSession(sessionSnapshot, fullTrackId);
                var committed = await sessionRepository.ImportProcessedSessionAsync(prepared, newTrack, source);
                var fresh = committed.Session
                    ?? throw new InvalidOperationException("The capture source exists, but its recorded session is no longer available.");

                var snapshot = SessionSnapshot.From(fresh);
                try
                {
                    await sessionPreferences.UpdateRecordedAsync(snapshot.Id, _ => preferences);
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to update recorded-session preferences after saving session {SessionId}", session.Id);
                }

                string? publicationWarning = null;
                try
                {
                    var tracks = await trackQueries.GetTrackSnapshotsAsync(snapshot.FullTrackId is { } id ? [id] : []);
                    await dispatcher.InvokeAsync(async () =>
                    {
                        using var batch = publication.BeginBatch();
                        await sessionStore.PublishSessionsChangedAsync([snapshot]);
                        await sourceStore.PublishSourcesChangedAsync([committed.Source]);
                        await trackStore.PublishTracksChangedAsync(tracks);
                    });
                }
                catch (Exception e)
                {
                    publicationWarning = $"Live session was saved, but app state could not be refreshed: {e.Message}";
                    logger.Warning(e, "Failed to publish live session state after saving session {SessionId}", session.Id);
                }

                logger.Information("Live session save completed for {SessionId}", session.Id);
                return new LiveSessionSaveResult.Saved(
                    snapshot.Id,
                    snapshot.Updated,
                    publicationWarning);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Live session save failed for {SessionId}", session.Id);
            return new LiveSessionSaveResult.Failed(LiveSessionSaveFailureReason.Save, e.Message);
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

    public sealed record Saved(Guid Id, long NewBaselineUpdated) : SessionSaveResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionSaveResult;
    public sealed record Missing : SessionSaveResult;
    public sealed record Failed(SessionSaveFailureReason Reason, string? Details = null) : SessionSaveResult;
}

public abstract record LiveSessionSaveResult
{
    private LiveSessionSaveResult() { }

    public sealed record Saved(
        Guid SessionId,
        long Updated,
        string? PublicationWarning = null) : LiveSessionSaveResult;
    public sealed record Failed(LiveSessionSaveFailureReason Reason, string? Details = null) : LiveSessionSaveResult;
    public sealed record ProcessingFailed(SessionProcessingFailureReason Reason, string? Details = null) : LiveSessionSaveResult;
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
    public sealed record SessionMissing : GpsOffsetResult;
    public sealed record Failed(GpsOffsetFailureReason Reason, string? Details = null) : GpsOffsetResult;
}
