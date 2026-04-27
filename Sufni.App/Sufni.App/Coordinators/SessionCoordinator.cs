
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the session feature workflow. Subscribes to the synchronization
/// server's session events in its constructor and keeps the
/// <see cref="ISessionStore"/> in sync.
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
    private readonly ITileLayerService tileLayerService;
    private readonly IShellCoordinator shell;
    private readonly IDialogService dialogService;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        IDatabaseService databaseService,
        IHttpApiService httpApiService,
        IBackgroundTaskRunner backgroundTaskRunner,
        TrackCoordinator trackCoordinator,
        ISessionPresentationService sessionPresentationService,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.sessionStore = sessionStore;
        this.databaseService = databaseService;
        this.httpApiService = httpApiService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.trackCoordinator = trackCoordinator;
        this.sessionPresentationService = sessionPresentationService;
        this.tileLayerService = tileLayerService;
        this.shell = shell;
        this.dialogService = dialogService;

        if (synchronizationServer is not null)
        {
            synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
            synchronizationServer.SessionDataArrived += OnSessionDataArrived;
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
                tileLayerService,
                shell,
                dialogService));
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
                if (cachedTelemetryData is null)
                {
                    logger.Warning("Mobile session cache is present but local telemetry could not be read for {SessionId}", sessionId);
                }

                logger.Information("Mobile session load completed from cache for {SessionId}", sessionId);
                return new SessionMobileLoadResult.LoadedFromCache(
                    SessionCachePresentationData.FromCache(cached),
                    cachedTelemetryData);
            }

            logger.Verbose("Mobile session cache miss for {SessionId}", sessionId);
            var telemetryData = await EnsureTelemetryDataAvailableForLoadAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                logger.Warning("Mobile session load is waiting for telemetry data for {SessionId}", sessionId);
                return new SessionMobileLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            logger.Verbose("Resolving track data for mobile session {SessionId}", sessionId);
            _ = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                fullTrackId,
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
            return new SessionMobileLoadResult.BuiltCache(presentation, telemetryData);
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
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting live session save for {SessionId}", session.Id);

        try
        {
            var telemetryData = await backgroundTaskRunner.RunAsync(
                () => TelemetryData.FromLiveCapture(capture.TelemetryCapture),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Guid? fullTrackId = null;
            if (telemetryData.GpsData is { Length: > 0 })
            {
                var track = await backgroundTaskRunner.RunAsync(
                    () => Track.FromGpsRecords(telemetryData.GpsData),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (track is not null)
                {
                    await databaseService.PutAsync(track);
                    fullTrackId = track.Id;
                }
            }

            session.ProcessedData = telemetryData.BinaryForm;
            session.FullTrack = fullTrackId;

            await databaseService.PutSessionAsync(session);
            var fresh = await databaseService.GetSessionAsync(session.Id);
            if (fresh is null)
            {
                logger.Error("Live session save failed because the session disappeared after save for {SessionId}", session.Id);
                return new LiveSessionSaveResult.Failed("Session disappeared after save");
            }

            var snapshot = SessionSnapshot.From(fresh);
            sessionStore.Upsert(snapshot);

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

public sealed record SessionDeleteResult(SessionDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SessionDeleteOutcome
{
    Deleted,
    Failed
}
