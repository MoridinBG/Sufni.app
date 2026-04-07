using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the session feature workflow. Subscribes to the
/// synchronization server's session events in its constructor and
/// keeps the <see cref="ISessionStore"/> in sync. Registered as a
/// singleton; eagerly resolved at app startup so the constructor's
/// event subscriptions actually run (see step 2.9 in REFACTOR-PLAN.md).
/// </summary>
public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly ISessionStoreWriter sessionStore;
    private readonly IDatabaseService databaseService;
    private readonly IHttpApiService httpApiService;
    private readonly IShellCoordinator shell;
    private readonly IDialogService dialogService;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        IDatabaseService databaseService,
        IHttpApiService httpApiService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.sessionStore = sessionStore;
        this.databaseService = databaseService;
        this.httpApiService = httpApiService;
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
                databaseService,
                shell,
                dialogService));
        return Task.CompletedTask;
    }

    public async Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated)
    {
        var current = sessionStore.Get(session.Id);
        if (current is not null && current.Updated > baselineUpdated)
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

        // Close any open editor BEFORE removing the snapshot so no
        // editor binding observes a missing row mid-teardown.
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

    private void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            foreach (var session in e.Data.Sessions)
            {
                if (session.Deleted is not null)
                {
                    sessionStore.Remove(session.Id);
                }
                else
                {
                    var fresh = await databaseService.GetSessionAsync(session.Id);
                    if (fresh is not null)
                    {
                        sessionStore.Upsert(SessionSnapshot.From(fresh));
                    }
                }
            }
        });
    }

    private void OnSessionDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var fresh = await databaseService.GetSessionAsync(e.SessionId);
            if (fresh is not null)
            {
                sessionStore.Upsert(SessionSnapshot.From(fresh));
            }
        });
    }
}
