
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns recorded-session workflows.
/// It opens session detail state, loads desktop and mobile telemetry, saves
/// metadata and live captures, recomputes derived data, deletes sessions, and
/// applies inbound session changes.
/// </summary>
public class SessionCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SessionCoordinator>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly IDatabaseService databaseService;
    private readonly IHttpApiService httpApiService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly TrackCoordinator trackCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly ISessionAnalysisService sessionAnalysisService;
    private readonly ITileLayerService tileLayerService;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IShellCoordinator shell;
    private readonly IDialogService dialogService;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IProcessingFingerprintService fingerprintService;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;
    private readonly IRecordedSessionGraph recordedSessionGraph;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        IDatabaseService databaseService,
        IHttpApiService httpApiService,
        IBackgroundTaskRunner backgroundTaskRunner,
        TrackCoordinator trackCoordinator,
        ISessionPresentationService sessionPresentationService,
        ISessionAnalysisService sessionAnalysisService,
        ITileLayerService tileLayerService,
        ISessionPreferences sessionPreferences,
        IShellCoordinator shell,
        IDialogService dialogService,
        IRecordedSessionSourceStoreWriter sourceStore,
        IProcessingFingerprintService fingerprintService,
        IRecordedSessionDomainQuery recordedSessionDomainQuery,
        IRecordedSessionGraph recordedSessionGraph,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.sessionStore = sessionStore;
        this.databaseService = databaseService;
        this.httpApiService = httpApiService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.trackCoordinator = trackCoordinator;
        this.sessionPresentationService = sessionPresentationService;
        this.sessionAnalysisService = sessionAnalysisService;
        this.tileLayerService = tileLayerService;
        this.sessionPreferences = sessionPreferences;
        this.shell = shell;
        this.dialogService = dialogService;
        this.sourceStore = sourceStore;
        this.fingerprintService = fingerprintService;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
        this.recordedSessionGraph = recordedSessionGraph;
        this.recordedSessionReprocessor = recordedSessionReprocessor;

        if (synchronizationServer is not null)
        {
            synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
            synchronizationServer.SessionDataArrived += OnSessionDataArrived;
            synchronizationServer.SessionSourceDataArrived += OnSessionSourceDataArrived;
        }
    }

    public virtual Task OpenEditAsync(Guid sessionId)
    {
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null) return Task.CompletedTask;

        shell.OpenOrFocus<SessionDetailViewModel>(
            editor => editor.Id == sessionId,
            () => new SessionDetailViewModel(
                snapshot,
                this,
                sessionStore,
                recordedSessionGraph,
                sessionPresentationService,
                sessionAnalysisService,
                tileLayerService,
                shell,
                dialogService,
                sessionPreferences));
        return Task.CompletedTask;
    }

    public virtual async Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting desktop session load for {SessionId}", sessionId);

        try
        {
            logger.Verbose("Loading telemetry data for desktop session {SessionId}", sessionId);
            var telemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                if (sessionStore.Get(sessionId) is { HasProcessedData: true })
                {
                    logger.Error(
                        "Desktop session load failed because telemetry data was marked present but could not be read for {SessionId}",
                        sessionId);
                    return new SessionDesktopLoadResult.Failed("Session data is marked as present but could not be read.");
                }

                logger.Warning("Desktop session load is waiting for telemetry data for {SessionId}", sessionId);
                return new SessionDesktopLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            logger.Verbose("Resolving track data for desktop session {SessionId}", sessionId);
            var trackData = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                fullTrackId,
                telemetryData,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            logger.Verbose("Calculating presentation data for desktop session {SessionId}", sessionId);
            var damperPercentages = await backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.CalculateDamperPercentages(telemetryData),
                cancellationToken);

            logger.Information("Desktop session load completed for {SessionId}", sessionId);
            return new SessionDesktopLoadResult.Loaded(
                new SessionTelemetryPresentationData(
                    telemetryData,
                    trackData.FullTrackId,
                    trackData.FullTrackPoints,
                    trackData.TrackPoints,
                    trackData.MapVideoWidth,
                    damperPercentages));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Desktop session load failed for {SessionId}", sessionId);
            return new SessionDesktopLoadResult.Failed(e.Message);
        }
    }

    public virtual async Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting mobile session load for {SessionId}", sessionId);

        try
        {
            logger.Verbose("Checking cached mobile presentation for session {SessionId}", sessionId);
            var cached = await backgroundTaskRunner.RunAsync(
                () => databaseService.GetSessionCacheAsync(sessionId),
                cancellationToken);
            if (cached is not null)
            {
                logger.Verbose("Mobile session cache hit for {SessionId}", sessionId);
                var cachedTelemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
                SessionTrackPresentationData? cachedTrackData = null;
                if (cachedTelemetryData is null)
                {
                    logger.Warning("Mobile session cache is present but local telemetry could not be read for {SessionId}", sessionId);
                }
                else
                {
                    var cachedFullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
                    logger.Verbose("Resolving cached mobile track data for session {SessionId}", sessionId);
                    cachedTrackData = await trackCoordinator.LoadSessionTrackAsync(
                        sessionId,
                        cachedFullTrackId,
                        cachedTelemetryData,
                        cancellationToken);
                }

                logger.Information("Mobile session load completed from cache for {SessionId}", sessionId);
                return new SessionMobileLoadResult.LoadedFromCache(
                    SessionCachePresentationData.FromCache(cached),
                    cachedTelemetryData,
                    cachedTrackData);
            }

            logger.Verbose("Mobile session cache miss for {SessionId}", sessionId);
            var telemetryData = await EnsureTelemetryDataAvailableForLoadAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                logger.Warning("Mobile session load is waiting for telemetry data for {SessionId}", sessionId);
                return new SessionMobileLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var mobileFullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            logger.Verbose("Resolving track data for mobile session {SessionId}", sessionId);
            var trackData = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                mobileFullTrackId,
                telemetryData,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            logger.Verbose("Building mobile session cache for {SessionId}", sessionId);
            var presentation = await backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.BuildCachePresentation(telemetryData, dimensions, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            logger.Verbose("Persisting mobile session cache for {SessionId}", sessionId);
            await backgroundTaskRunner.RunAsync(
                () => databaseService.PutSessionCacheAsync(presentation.ToCache(sessionId)),
                cancellationToken);

            logger.Information("Mobile session load completed for {SessionId}", sessionId);
            return new SessionMobileLoadResult.BuiltCache(presentation, telemetryData, trackData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Mobile session load failed for {SessionId}", sessionId);
            return new SessionMobileLoadResult.Failed(e.Message);
        }
    }

    public virtual async Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated)
    {
        logger.Information("Starting session save for {SessionId}", session.Id);

        var current = sessionStore.Get(session.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            logger.Warning("Session save conflict for {SessionId}", session.Id);
            return new SessionSaveResult.Conflict(current);
        }

        try
        {
            if (current is not null && session.ProcessingFingerprintJson is null)
            {
                session.ProcessingFingerprintJson = current.ProcessingFingerprintJson;
            }

            await databaseService.PutSessionAsync(session);
            // PutSessionAsync only writes metadata columns; re-fetch via
            // the SQL-computed has_data path so the snapshot's
            // HasProcessedData reflects the current DB state.
            var fresh = await databaseService.GetSessionAsync(session.Id);
            if (fresh is null)
            {
                logger.Error("Session save failed because the session disappeared after save for {SessionId}", session.Id);
                return new SessionSaveResult.Failed("Session disappeared after save");
            }
            var saved = SessionSnapshot.From(fresh);
            sessionStore.Upsert(saved);
            shell.GoBack();

            logger.Information("Session save completed for {SessionId}", session.Id);
            return new SessionSaveResult.Saved(saved.Updated);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session save failed for {SessionId}", session.Id);
            return new SessionSaveResult.Failed(e.Message);
        }
    }

    public virtual async Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        logger.Information("Starting live session save for {SessionId}", session.Id);

        try
        {
            var telemetryData = await backgroundTaskRunner.RunAsync(
                () => TelemetryData.FromLiveCapture(capture.TelemetryCapture),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Track? fullTrack = null;
            if (telemetryData.GpsData is { Length: > 0 })
            {
                fullTrack = await backgroundTaskRunner.RunAsync(
                    () => Track.FromGpsRecords(telemetryData.GpsData),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }

            var source = CreateLiveCaptureSource(session.Id, capture.TelemetryCapture);
            var setup = await databaseService.GetAsync<Setup>(capture.Context.SetupId)
                        ?? throw new InvalidOperationException("Setup is missing.");
            var bike = await databaseService.GetAsync<Bike>(setup.BikeId)
                       ?? throw new InvalidOperationException("Bike is missing.");
            var setupSnapshot = SetupSnapshot.From(setup, boardId: null);
            var bikeSnapshot = BikeSnapshot.From(bike);
            var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
            var sessionSnapshot = SessionSnapshot.From(session);
            var fingerprint = fingerprintService.CreateCurrent(
                sessionSnapshot,
                setupSnapshot,
                bikeSnapshot,
                sourceSnapshot);

            session.ProcessedData = telemetryData.BinaryForm;
            session.ProcessingFingerprintJson = AppJson.Serialize(fingerprint);

            var fresh = await databaseService.PutProcessedSessionAsync(session, fullTrack, source);

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

    public virtual async Task<SessionRecomputeResult> RecomputeAsync(
        Guid sessionId,
        long baselineUpdated,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting recorded session recompute for {SessionId}", sessionId);

        try
        {
            var domain = recordedSessionDomainQuery.Get(sessionId);
            if (domain is null)
            {
                logger.Warning("Recorded session recompute failed because session {SessionId} is missing", sessionId);
                return new SessionRecomputeResult.Failed("Session is missing.");
            }

            if (domain.Session.Updated > baselineUpdated)
            {
                logger.Warning("Recorded session recompute conflict for {SessionId}", sessionId);
                return new SessionRecomputeResult.Conflict(domain.Session);
            }

            if (!domain.Staleness.CanRecompute)
            {
                logger.Warning("Recorded session {SessionId} is not recomputable because {Reason}", sessionId, domain.Staleness.GetType().Name);
                return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
            }

            var source = await sourceStore.LoadAsync(sessionId, cancellationToken);
            if (source is null)
            {
                logger.Warning("Recorded session recompute failed because source {SessionId} is missing", sessionId);
                return new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource());
            }

            var loadedSourceSnapshot = RecordedSessionSourceSnapshot.From(source);
            if (domain.Source != loadedSourceSnapshot)
            {
                sourceStore.Upsert(loadedSourceSnapshot);
                domain = recordedSessionDomainQuery.Get(sessionId);
                if (domain is null)
                {
                    logger.Warning("Recorded session recompute failed because session {SessionId} disappeared after source refresh", sessionId);
                    return new SessionRecomputeResult.Failed("Session is missing.");
                }

                if (domain.Session.Updated > baselineUpdated)
                {
                    logger.Warning("Recorded session recompute conflict for {SessionId} after source refresh", sessionId);
                    return new SessionRecomputeResult.Conflict(domain.Session);
                }

                if (!domain.Staleness.CanRecompute)
                {
                    logger.Warning("Recorded session {SessionId} is not recomputable after source refresh because {Reason}", sessionId, domain.Staleness.GetType().Name);
                    return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
                }
            }

            var reprocessResult = await backgroundTaskRunner.RunAsync(
                () => recordedSessionReprocessor.ReprocessAsync(domain, source, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var persisted = await databaseService.GetSessionAsync(sessionId);
            if (persisted is null)
            {
                logger.Warning("Recorded session recompute failed because session {SessionId} disappeared before persistence", sessionId);
                return new SessionRecomputeResult.Failed("Session is missing.");
            }

            if (persisted.Updated > baselineUpdated)
            {
                var current = SessionSnapshot.From(persisted);
                logger.Warning("Recorded session recompute conflict for {SessionId} before persistence", sessionId);
                return new SessionRecomputeResult.Conflict(current);
            }

            var previousFullTrackId = persisted.FullTrack;
            var previousFullTrack = previousFullTrackId.HasValue
                ? await databaseService.GetAsync<Track>(previousFullTrackId.Value)
                : null;
            Track? newFullTrack = null;

            if (reprocessResult.GeneratedFullTrack is null)
            {
                persisted.FullTrack = null;
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

            persisted.ProcessedData = reprocessResult.TelemetryData.BinaryForm;
            persisted.ProcessingFingerprintJson = AppJson.Serialize(reprocessResult.Fingerprint);

            var fresh = await databaseService.PutProcessedSessionIfUnchangedAsync(
                persisted,
                newFullTrack,
                source: null,
                baselineUpdated);
            if (fresh is null)
            {
                var current = await databaseService.GetSessionAsync(sessionId);
                if (current is null)
                {
                    return new SessionRecomputeResult.Failed("Session is missing.");
                }

                return new SessionRecomputeResult.Conflict(SessionSnapshot.From(current));
            }

            var snapshot = SessionSnapshot.From(fresh);
            sessionStore.Upsert(snapshot);

            await DeletePreviousFullTrackIfOrphanedAsync(previousFullTrackId, fresh.FullTrack, sessionId);

            logger.Information("Recorded session recompute completed for {SessionId}", sessionId);
            return new SessionRecomputeResult.Recomputed(snapshot.Updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Recorded session recompute failed for {SessionId}", sessionId);
            return new SessionRecomputeResult.Failed(e.Message);
        }
    }

    public virtual async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
    {
        logger.Information("Starting session delete for {SessionId}", sessionId);

        try
        {
            var session = await databaseService.GetSessionAsync(sessionId);
            var trackId = session?.FullTrack;
            var shouldDeleteTrack = false;

            if (trackId.HasValue)
            {
                var sessions = await databaseService.GetAllAsync<Session>();
                shouldDeleteTrack = !sessions.Any(existing => existing.Id != sessionId && existing.FullTrack == trackId);
            }

            await databaseService.DeleteAsync<Session>(sessionId);
            await sourceStore.RemoveAsync(sessionId);

            if (shouldDeleteTrack && trackId.HasValue)
            {
                try
                {
                    await databaseService.DeleteAsync<Track>(trackId.Value);
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

        shell.CloseIfOpen<SessionDetailViewModel>(editor => editor.Id == sessionId);
        sessionStore.Remove(sessionId);
        logger.Information("Session delete completed for {SessionId}", sessionId);
        return new SessionDeleteResult(SessionDeleteOutcome.Deleted);
    }

    private Task<TelemetryData?> LoadTelemetryDataAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return backgroundTaskRunner.RunAsync(
            () => databaseService.GetSessionPsstAsync(sessionId),
            cancellationToken);
    }

    private async Task DeletePreviousFullTrackIfOrphanedAsync(Guid? previousFullTrackId, Guid? currentFullTrackId, Guid sessionId)
    {
        if (!previousFullTrackId.HasValue || previousFullTrackId == currentFullTrackId)
        {
            return;
        }

        var sessions = await databaseService.GetAllAsync<Session>();
        var stillReferenced = sessions.Any(existing =>
            existing.Id != sessionId &&
            existing.Deleted is null &&
            existing.FullTrack == previousFullTrackId.Value);
        if (stillReferenced)
        {
            return;
        }

        try
        {
            await databaseService.DeleteAsync<Track>(previousFullTrackId.Value);
        }
        catch (Exception e)
        {
            logger.Warning(e, "Failed to delete orphaned track {TrackId} after recomputing session {SessionId}", previousFullTrackId.Value, sessionId);
        }
    }

    private static RecordedSessionSource CreateLiveCaptureSource(Guid sessionId, LiveTelemetryCapture capture)
    {
        const int schemaVersion = 1;
        var payload = new RecordedLiveCaptureSourcePayload(
            schemaVersion,
            capture.Metadata,
            capture.FrontMeasurements,
            capture.RearMeasurements,
            capture.ImuData,
            capture.GpsData,
            capture.Markers);
        var payloadBytes = Encoding.UTF8.GetBytes(AppJson.Serialize(payload));

        return new RecordedSessionSource
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.LiveCapture,
            SourceName = capture.Metadata.SourceName,
            SchemaVersion = schemaVersion,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.LiveCapture,
                capture.Metadata.SourceName,
                schemaVersion,
                payloadBytes),
            Payload = payloadBytes
        };
    }

    private async Task<TelemetryData?> EnsureTelemetryDataAvailableForLoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var telemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
        if (telemetryData is not null)
        {
            logger.Verbose("Telemetry data cache hit for session {SessionId}", sessionId);
            return telemetryData;
        }

        var current = sessionStore.Get(sessionId);
        if (current is { HasProcessedData: true })
        {
            throw new InvalidOperationException("Session data is marked as present but could not be read.");
        }

        logger.Verbose("Downloading telemetry data during load for session {SessionId}", sessionId);
        var psst = await httpApiService.GetSessionPsstAsync(sessionId);
        if (psst is null)
        {
            logger.Warning("Telemetry data is not yet available from the server for session {SessionId}", sessionId);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await backgroundTaskRunner.RunAsync(
            () => databaseService.PatchSessionPsstAsync(sessionId, psst),
            cancellationToken);

        var fresh = await backgroundTaskRunner.RunAsync(
            () => databaseService.GetSessionAsync(sessionId),
            cancellationToken);
        if (fresh is not null)
        {
            sessionStore.Upsert(SessionSnapshot.From(fresh));
        }

        logger.Verbose("Reloading telemetry data after server download for session {SessionId}", sessionId);
        return await LoadTelemetryDataAsync(sessionId, cancellationToken);
    }

    private async void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        try
        {
            await HandleSynchronizationDataArrivedAsync(e);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound session synchronization data");
        }
    }

    private async Task HandleSynchronizationDataArrivedAsync(SynchronizationDataArrivedEventArgs e)
    {
        var removals = new List<Guid>();
        var upserts = new List<SessionSnapshot>();

        foreach (var session in e.Data.Sessions)
        {
            if (session.Deleted is not null)
            {
                removals.Add(session.Id);
                continue;
            }

            var fresh = await databaseService.GetSessionAsync(session.Id);
            if (fresh is not null)
            {
                upserts.Add(SessionSnapshot.From(fresh));
            }
        }

        logger.Verbose(
            "Applying inbound session synchronization with {RemovalCount} removals and {UpsertCount} upserts",
            removals.Count,
            upserts.Count);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var id in removals)
            {
                sessionStore.Remove(id);
            }

            foreach (var snapshot in upserts)
            {
                sessionStore.Upsert(snapshot);
            }
        });
    }

    private async void OnSessionDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        try
        {
            await HandleSessionDataArrivedAsync(e);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound session data for {SessionId}", e.SessionId);
        }
    }

    private async Task HandleSessionDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        logger.Verbose("Applying inbound session data for {SessionId}", e.SessionId);

        var fresh = await databaseService.GetSessionAsync(e.SessionId);
        if (fresh is null)
        {
            logger.Verbose("Ignoring inbound session data because session {SessionId} is missing", e.SessionId);
            return;
        }

        var snapshot = SessionSnapshot.From(fresh);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            sessionStore.Upsert(snapshot);
        });
    }

    private async void OnSessionSourceDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        try
        {
            await HandleSessionSourceDataArrivedAsync(e);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound recorded source for {SessionId}", e.SessionId);
        }
    }

    private async Task HandleSessionSourceDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        logger.Verbose("Applying inbound recorded source for {SessionId}", e.SessionId);

        var source = await databaseService.GetRecordedSessionSourceAsync(e.SessionId);
        if (source is null)
        {
            logger.Verbose("Ignoring inbound recorded source because source {SessionId} is missing", e.SessionId);
            return;
        }

        var snapshot = RecordedSessionSourceSnapshot.From(source);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            sourceStore.Upsert(snapshot);
        });
    }
}

public abstract record SessionSaveResult
{
    private SessionSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated) : SessionSaveResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionSaveResult;
    public sealed record Failed(string ErrorMessage) : SessionSaveResult;
}

public abstract record LiveSessionSaveResult
{
    private LiveSessionSaveResult() { }

    public sealed record Saved(Guid SessionId, long Updated) : LiveSessionSaveResult;
    public sealed record Failed(string ErrorMessage) : LiveSessionSaveResult;
}

/// <summary>
/// Result of attempting to rebuild a recorded session's derived telemetry.
/// It distinguishes successful recompute, optimistic-concurrency conflict,
/// unrecomputable current state, and failure.
/// </summary>
public abstract record SessionRecomputeResult
{
    private SessionRecomputeResult() { }

    public sealed record Recomputed(long NewBaselineUpdated) : SessionRecomputeResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionRecomputeResult;
    public sealed record NotRecomputable(SessionStaleness Reason) : SessionRecomputeResult;
    public sealed record Failed(string ErrorMessage) : SessionRecomputeResult;
}

public sealed record SessionDeleteResult(SessionDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SessionDeleteOutcome
{
    Deleted,
    Failed
}
