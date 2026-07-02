using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Sessions.Coordination;

/// <summary>
/// Owns every store-writing recorded-session command: metadata save (with the
/// in-memory store-snapshot optimistic-concurrency check), delete, live-capture
/// save, and recompute requests (delegated to the recompute engine, the single
/// owner of recompute liveness). Read-only loads live in
/// <see cref="SessionLoader"/> and inbound sync in <see cref="SessionSyncApplier"/>.
/// </summary>
public sealed class SessionCommandService
{
    private static readonly ILogger logger = Log.ForContext<SessionCommandService>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly ISynchronizableRepository<Bike> bikeRepository;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly ISynchronizableRepository<Session> sessionEntityRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IShellCoordinator shell;
    private readonly ISessionRecomputeEngine recomputeEngine;
    private readonly Func<IEditorFactory> editorFactory;

    public SessionCommandService(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        ISynchronizableRepository<Setup> setupRepository,
        ISynchronizableRepository<Bike> bikeRepository,
        ISynchronizableRepository<Track> trackEntityRepository,
        ISynchronizableRepository<Session> sessionEntityRepository,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        IShellCoordinator shell,
        ISessionRecomputeEngine recomputeEngine,
        Func<IEditorFactory> editorFactory)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.setupRepository = setupRepository;
        this.bikeRepository = bikeRepository;
        this.trackEntityRepository = trackEntityRepository;
        this.sessionEntityRepository = sessionEntityRepository;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.shell = shell;
        this.recomputeEngine = recomputeEngine;
        this.editorFactory = editorFactory;
    }

    public Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason) =>
        recomputeEngine.RequestRecomputeAsync(sessionId, reason);

    public Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        RecomputeReason reason,
        IProgress<SessionRecomputeAllProgress>? progress = null) =>
        recomputeEngine.RequestRecomputeAllAsync(reason, progress);

    public bool IsRecomputeActive(Guid sessionId) => recomputeEngine.IsActive(sessionId);

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

        try
        {
            // The fingerprint is a derived column preserved by PutSessionAsync from
            // the existing row, so the metadata save no longer needs to copy it
            // forward from the store snapshot.
            await sessionRepository.PutSessionAsync(session);
            // Re-fetch via the SQL-computed has_data path so the snapshot's
            // HasProcessedData reflects the current DB state.
            var fresh = await sessionRepository.GetSessionAsync(session.Id);
            if (fresh is null)
            {
                logger.Error("Session save failed because the session disappeared after save for {SessionId}", session.Id);
                return new SessionSaveResult.Failed("Session disappeared after save");
            }
            var saved = SessionSnapshot.From(fresh);
            sessionStore.Upsert(saved);
            _ = shell.GoBack();

            logger.Information("Session save completed for {SessionId}", session.Id);
            return new SessionSaveResult.Saved(saved.Updated);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session save failed for {SessionId}", session.Id);
            return new SessionSaveResult.Failed(e.Message);
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
            var processingOptions = preferences.Processing.ToTelemetryProcessingOptions();
            var source = RecordedSessionSourceFactory.CreateLiveCapture(session.Id, capture.TelemetryCapture);
            var setup = await setupRepository.GetAsync(capture.Context.SetupId)
                        ?? throw new InvalidOperationException("Setup is missing.");
            var bike = await bikeRepository.GetAsync(setup.BikeId)
                       ?? throw new InvalidOperationException("Bike is missing.");
            var setupSnapshot = SetupSnapshot.From(setup, boardId: null);
            var bikeSnapshot = BikeSnapshot.From(bike);
            var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
            var sessionSnapshot = SessionSnapshot.From(session);
            var domain = new RecordedSessionDomainSnapshot(
                sessionSnapshot,
                setupSnapshot,
                bikeSnapshot,
                CurrentFingerprint: null,
                PersistedFingerprint: null,
                sourceSnapshot,
                new SessionStaleness.UnknownLegacyFingerprint(),
                DerivedChangeKind.None);

            var reprocessResult = await backgroundTaskRunner.RunAsync(
                () => recordedSessionReprocessor.ReprocessAsync(domain, source, processingOptions, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var fresh = await sessionTelemetryWriter.PutProcessedSessionAsync(
                session,
                reprocessResult.ProcessedTelemetry,
                reprocessResult.GeneratedFullTrack,
                source);

            var snapshot = SessionSnapshot.From(fresh);
            await sessionPreferences.UpdateRecordedAsync(snapshot.Id, _ => preferences);

            sessionStore.Upsert(snapshot);
            sourceStore.Upsert(sourceSnapshot);

            logger.Information("Live session save completed for {SessionId}", session.Id);
            return new LiveSessionSaveResult.Saved(snapshot.Id, snapshot.Updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Live session save failed for {SessionId}", session.Id);
            return new LiveSessionSaveResult.Failed(e.Message);
        }
    }

    public async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
    {
        logger.Information("Starting session delete for {SessionId}", sessionId);

        try
        {
            var session = await sessionRepository.GetSessionAsync(sessionId);
            var trackId = session?.FullTrack;
            var shouldDeleteTrack = false;

            if (trackId.HasValue)
            {
                shouldDeleteTrack = !await sessionRepository.HasOtherActiveSessionWithFullTrackAsync(
                    trackId.Value,
                    sessionId);
            }

            await sessionEntityRepository.DeleteAsync(sessionId);
            await recordedSessionSourceRepository.DeleteRecordedSessionSourceAsync(sessionId);
            sourceStore.Remove(sessionId);

            if (shouldDeleteTrack && trackId.HasValue)
            {
                try
                {
                    await trackEntityRepository.DeleteAsync(trackId.Value);
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to delete orphaned track {TrackId} after deleting session {SessionId}", trackId.Value, sessionId);
                }
            }

            await sessionPreferences.RemoveRecordedAsync(sessionId);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session delete failed for {SessionId}", sessionId);
            return new SessionDeleteResult(SessionDeleteOutcome.Failed, e.Message);
        }

        editorFactory().CloseSessionDetail(sessionId);
        sessionStore.Remove(sessionId);
        logger.Information("Session delete completed for {SessionId}", sessionId);
        return new SessionDeleteResult(SessionDeleteOutcome.Deleted);
    }
}
