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
using Sufni.App.Shared.Stores;
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
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IShellCoordinator shell;
    private readonly IAppEnvironment appEnvironment;
    private readonly ISessionRecomputeEngine recomputeEngine;
    private readonly Func<IEditorFactory> editorFactory;
    private readonly IRecordedSessionDerivationWindowCache derivationWindowCache;
    private readonly IRecordedSessionDerivationWindowProvider derivationWindowProvider;
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions;

    public SessionCommandService(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        ISynchronizableRepository<Setup> setupRepository,
        ISynchronizableRepository<Bike> bikeRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        IShellCoordinator shell,
        IAppEnvironment appEnvironment,
        ISessionRecomputeEngine recomputeEngine,
        Func<IEditorFactory> editorFactory,
        IRecordedSessionDerivationWindowCache derivationWindowCache,
        IRecordedSessionDerivationWindowProvider derivationWindowProvider,
        ISessionPersistenceTransactionRunner sessionPersistenceTransactions)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.setupRepository = setupRepository;
        this.bikeRepository = bikeRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.shell = shell;
        this.appEnvironment = appEnvironment;
        this.recomputeEngine = recomputeEngine;
        this.editorFactory = editorFactory;
        this.derivationWindowCache = derivationWindowCache;
        this.derivationWindowProvider = derivationWindowProvider;
        this.sessionPersistenceTransactions = sessionPersistenceTransactions;
    }

    public Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason) =>
        recomputeEngine.RequestRecomputeAsync(sessionId, reason);

    public Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        RecomputeReason reason,
        IProgress<SessionRecomputeAllProgress>? progress = null) =>
        recomputeEngine.RequestRecomputeAllAsync(reason, progress);

    public bool IsRecomputeActive(Guid sessionId) => recomputeEngine.IsActive(sessionId);

    public async Task<Guid?> CreateDerivedSessionAsync(
        Guid fromSessionId,
        string name,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var from = sessionStore.Get(fromSessionId);
        if (from is null)
        {
            return null;
        }

        try
        {
            var origin = CalculateSourceAbsoluteOrigin(from, sourceAbsoluteStartSeconds);
            var derived = new Session(Guid.NewGuid(), name, from.Description, from.SetupId, origin.Timestamp)
            {
                GpsOffsetSeconds = origin.GpsOffsetSeconds,
                FrontSpringRate = from.FrontSpringRate,
                FrontHighSpeedCompression = from.FrontHighSpeedCompression,
                FrontLowSpeedCompression = from.FrontLowSpeedCompression,
                FrontLowSpeedRebound = from.FrontLowSpeedRebound,
                FrontHighSpeedRebound = from.FrontHighSpeedRebound,
                RearSpringRate = from.RearSpringRate,
                RearHighSpeedCompression = from.RearHighSpeedCompression,
                RearLowSpeedCompression = from.RearLowSpeedCompression,
                RearLowSpeedRebound = from.RearLowSpeedRebound,
                RearHighSpeedRebound = from.RearHighSpeedRebound,
            };

            var result = await sessionStore.CommitDerivedSessionAsync(derived, cancellationToken);
            return result is StoreMutationResult<SessionSnapshot>.Saved saved
                ? saved.Snapshot.Id
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Creating derived session from {SessionId} failed", fromSessionId);
            return null;
        }
    }

    public async Task<bool> UpdateSessionOriginAsync(
        Guid sessionId,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null)
        {
            return false;
        }

        try
        {
            var origin = CalculateSourceAbsoluteOrigin(snapshot, sourceAbsoluteStartSeconds);
            var session = snapshot.ToMetadataEntity();
            session.Timestamp = origin.Timestamp;
            session.GpsOffsetSeconds = origin.GpsOffsetSeconds;

            var result = await sessionStore.CommitSessionMetadataAsync(
                session,
                snapshot.Updated,
                cancellationToken);
            return result is StoreMutationResult<SessionSnapshot>.Saved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Updating session origin failed for {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> RenameSessionAsync(
        Guid sessionId,
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null)
        {
            return false;
        }

        try
        {
            var session = snapshot.ToMetadataEntity();
            session.Name = name;

            var result = await sessionStore.CommitSessionMetadataAsync(
                session,
                snapshot.Updated,
                cancellationToken);
            return result is StoreMutationResult<SessionSnapshot>.Saved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Renaming session failed for {SessionId}", sessionId);
            return false;
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

        try
        {
            var commit = await sessionStore.CommitSessionMetadataAsync(session, baselineUpdated);
            if (commit is StoreMutationResult<SessionSnapshot>.Conflict conflict)
            {
                logger.Warning("Session save conflict for {SessionId}", session.Id);
                return new SessionSaveResult.Conflict(conflict.CurrentSnapshot);
            }

            if (commit is not StoreMutationResult<SessionSnapshot>.Saved savedResult)
            {
                var message = commit switch
                {
                    StoreMutationResult<SessionSnapshot>.Missing missing => missing.ErrorMessage,
                    StoreMutationResult<SessionSnapshot>.Failed failed => failed.ErrorMessage,
                    _ => "Session save failed"
                };
                logger.Error("Session save failed for {SessionId}: {ErrorMessage}", session.Id, message);
                return new SessionSaveResult.Failed(message);
            }

            var saved = savedResult.Snapshot;
            if (appEnvironment.LayoutProfile == UiLayoutProfile.Compact)
            {
                _ = shell.GoBack();
            }

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
                DerivationWindow: null,
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
            try
            {
                await sessionPreferences.UpdateRecordedAsync(snapshot.Id, _ => preferences);
            }
            catch (Exception e)
            {
                logger.Warning(e, "Failed to update recorded-session preferences after saving session {SessionId}", session.Id);
            }

            await sessionStore.PublishSessionsChangedAsync([snapshot.Id], cancellationToken);
            await sourceStore.PublishSourcesChangedAsync([sourceSnapshot.SessionId], cancellationToken);

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

    private (long? Timestamp, double GpsOffsetSeconds) CalculateSourceAbsoluteOrigin(
        SessionSnapshot snapshot,
        double sourceAbsoluteStartSeconds)
    {
        if (!double.IsFinite(sourceAbsoluteStartSeconds) || sourceAbsoluteStartSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAbsoluteStartSeconds));
        }

        if (snapshot.Timestamp is not { } timestamp)
        {
            return (null, 0);
        }

        var currentStartSeconds = derivationWindowCache.Get(snapshot.Id)?.StartSeconds ?? 0;
        var rawTimestamp = timestamp - (long)Math.Floor(currentStartSeconds);
        var alignmentBase = snapshot.GpsOffsetSeconds - FractionalSeconds(currentStartSeconds);
        return (
            rawTimestamp + (long)Math.Floor(sourceAbsoluteStartSeconds),
            alignmentBase + FractionalSeconds(sourceAbsoluteStartSeconds));
    }

    private static double FractionalSeconds(double seconds) => seconds - Math.Floor(seconds);

    public async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
    {
        logger.Information("Starting session delete for {SessionId}", sessionId);

        try
        {
            var session = await sessionRepository.GetSessionAsync(sessionId);
            var trackId = session?.FullTrack;
            var shouldDeleteTrack = false;
            var shouldDeleteSource = !await derivationWindowProvider.IsRecordingSourceReferencedAsync(sessionId);

            if (trackId.HasValue)
            {
                shouldDeleteTrack = !await sessionRepository.HasOtherActiveSessionWithFullTrackAsync(
                    trackId.Value,
                    sessionId);
            }

            await sessionPersistenceTransactions.DeleteSessionAsync(
                sessionId,
                trackId,
                shouldDeleteTrack,
                shouldDeleteSource);

            if (shouldDeleteSource)
            {
                await sourceStore.PublishSourcesRemovedAsync([sessionId]);
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "Session delete failed for {SessionId}", sessionId);
            return new SessionDeleteResult(SessionDeleteOutcome.Failed, e.Message);
        }

        try
        {
            await sessionPreferences.RemoveRecordedAsync(sessionId);
        }
        catch (Exception e)
        {
            logger.Warning(e, "Failed to remove recorded-session preferences after deleting session {SessionId}", sessionId);
        }

        await editorFactory().CloseSessionDetail(sessionId);
        await sessionStore.PublishSessionsRemovedAsync([sessionId]);
        logger.Information("Session delete completed for {SessionId}", sessionId);
        return new SessionDeleteResult(SessionDeleteOutcome.Deleted);
    }
}
