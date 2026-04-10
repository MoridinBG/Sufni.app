
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the session feature workflow. Subscribes to the synchronization
/// server's session events in its constructor and keeps the
/// <see cref="ISessionStore"/> in sync.
/// </summary>
public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly ISessionStoreWriter sessionStore;
    private readonly IDatabaseService databaseService;
    private readonly IHttpApiService httpApiService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ITrackCoordinator trackCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IShellCoordinator shell;
    private readonly IDialogService dialogService;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        IDatabaseService databaseService,
        IHttpApiService httpApiService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ITrackCoordinator trackCoordinator,
        ISessionPresentationService sessionPresentationService,
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
        this.shell = shell;
        this.dialogService = dialogService;

        if (synchronizationServer is not null)
        {
            // Use += so the bike/setup handler set on the same property
            // by MainPagesViewModel is preserved alongside this one.
            synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
            synchronizationServer.SessionDataArrived += OnSessionDataArrived;
        }
    }

    public Task OpenEditAsync(Guid sessionId)
    {
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null) return Task.CompletedTask;

        shell.OpenOrFocus<SessionDetailViewModel>(
            editor => editor.Id == sessionId,
            () => new SessionDetailViewModel(
                snapshot,
                this,
                sessionStore,
                shell,
                dialogService));
        return Task.CompletedTask;
    }

    public async Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var telemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                if (sessionStore.Get(sessionId) is { HasProcessedData: true })
                {
                    return new SessionDesktopLoadResult.Failed("Session data is marked as present but could not be read.");
                }

                return new SessionDesktopLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            var trackData = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                fullTrackId,
                telemetryData,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var damperPercentages = await backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.CalculateDamperPercentages(telemetryData),
                cancellationToken);

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
            return new SessionDesktopLoadResult.Failed(e.Message);
        }
    }

    public async Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cached = await backgroundTaskRunner.RunAsync(
                () => databaseService.GetSessionCacheAsync(sessionId),
                cancellationToken);
            if (cached is not null)
            {
                return new SessionMobileLoadResult.LoadedFromCache(SessionCachePresentationData.FromCache(cached));
            }

            var telemetryData = await EnsureTelemetryDataAvailableForLoadAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                return new SessionMobileLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            _ = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                fullTrackId,
                telemetryData,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var presentation = await backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.BuildCachePresentation(telemetryData, dimensions, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await backgroundTaskRunner.RunAsync(
                () => databaseService.PutSessionCacheAsync(presentation.ToCache(sessionId)),
                cancellationToken);

            return new SessionMobileLoadResult.BuiltCache(presentation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new SessionMobileLoadResult.Failed(e.Message);
        }
    }

    public async Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated)
    {
        var current = sessionStore.Get(session.Id);
        if (current.IsNewerThan(baselineUpdated))
        {
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
                return new SessionSaveResult.Failed("Session disappeared after save");
            }
            var saved = SessionSnapshot.From(fresh);
            sessionStore.Upsert(saved);

            return new SessionSaveResult.Saved(saved.Updated);
        }
        catch (Exception e)
        {
            return new SessionSaveResult.Failed(e.Message);
        }
    }

    public async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
    {
        try
        {
            await databaseService.DeleteAsync<Session>(sessionId);
        }
        catch (Exception e)
        {
            return new SessionDeleteResult(SessionDeleteOutcome.Failed, e.Message);
        }

        shell.CloseIfOpen<SessionDetailViewModel>(editor => editor.Id == sessionId);
        sessionStore.Remove(sessionId);
        return new SessionDeleteResult(SessionDeleteOutcome.Deleted);
    }

    public async Task EnsureTelemetryDataAvailableAsync(Guid sessionId)
    {
        var current = sessionStore.Get(sessionId);
        if (current is { HasProcessedData: true }) return;

        var psst = await httpApiService.GetSessionPsstAsync(sessionId)
            ?? throw new Exception("Session data could not be downloaded from server.");
        await databaseService.PatchSessionPsstAsync(sessionId, psst);

        var fresh = await databaseService.GetSessionAsync(sessionId);
        if (fresh is not null)
        {
            sessionStore.Upsert(SessionSnapshot.From(fresh));
        }
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
            return telemetryData;
        }

        var current = sessionStore.Get(sessionId);
        if (current is { HasProcessedData: true })
        {
            throw new InvalidOperationException("Session data is marked as present but could not be read.");
        }

        var psst = await httpApiService.GetSessionPsstAsync(sessionId);
        if (psst is null)
        {
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

        return await LoadTelemetryDataAsync(sessionId, cancellationToken);
    }

    private async void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        try
        {
            await HandleSynchronizationDataArrivedAsync(e);
        }
        catch (Exception)
        {
            // TODO: Handle somehow? At least log
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
        catch (Exception)
        {
            // TODO: Handle somehow? At least log
        }
    }

    private async Task HandleSessionDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        var fresh = await databaseService.GetSessionAsync(e.SessionId);
        if (fresh is null)
        {
            return;
        }

        var snapshot = SessionSnapshot.From(fresh);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            sessionStore.Upsert(snapshot);
        });
    }
}
