using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;

using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

public sealed class SessionSyncApplier
{
    private static readonly ILogger logger = Log.ForContext<SessionSyncApplier>();

    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly IRecordedSessionSourceRepository sourceRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionStoreWriter sessionStore;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;

    public SessionSyncApplier(
        ISessionStoreWriter sessionStore,
        IRecordedSessionSourceStoreWriter sourceStore,
        IUiThreadDispatcher uiThreadDispatcher,
        IRecordedSessionSourceRepository sourceRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.sessionStore = sessionStore;
        this.sourceStore = sourceStore;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.sourceRepository = sourceRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;

        if (synchronizationServer is not null)
        {
            synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
            synchronizationServer.SessionDataArrived += OnSessionDataArrived;
            synchronizationServer.SessionSourceDataArrived += OnSessionSourceDataArrived;
        }
    }

    private async void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        try
        {
            await uiThreadDispatcher.InvokeAsync(() => HandleSynchronizationDataArrivedAsync(e));
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound session synchronization data");
        }
    }

    private async Task HandleSynchronizationDataArrivedAsync(SynchronizationDataArrivedEventArgs e)
    {
        var removals = new List<Guid>();
        var changes = new List<Guid>();

        foreach (var session in e.Data.Sessions)
        {
            if (session.Deleted is not null)
            {
                removals.Add(session.Id);
                continue;
            }

            changes.Add(session.Id);
        }

        logger.Verbose(
            "Applying inbound session synchronization with {RemovalCount} removals and {UpsertCount} upserts",
            removals.Count,
            changes.Count);

        if (removals.Count > 0)
        {
            await sessionStore.PublishSessionsRemovedAsync(removals);
        }

        if (changes.Count > 0)
        {
            await sessionStore.PublishSessionsChangedAsync(changes);
        }
    }

    private async void OnSessionDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        try
        {
            await uiThreadDispatcher.InvokeAsync(() => HandleSessionDataArrivedAsync(e));
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound session data for {SessionId}", e.SessionId);
        }
    }

    private async Task HandleSessionDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        logger.Verbose("Applying inbound session data for {SessionId}", e.SessionId);

        await sessionStore.PublishSessionsChangedAsync([e.SessionId]);
    }

    private async void OnSessionSourceDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        try
        {
            await uiThreadDispatcher.InvokeAsync(() => HandleSessionSourceDataArrivedAsync(e));
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound recorded source for {SessionId}", e.SessionId);
        }
    }

    private async Task HandleSessionSourceDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        logger.Verbose("Applying inbound recorded source for {SessionId}", e.SessionId);

        var snapshot = await backgroundTaskRunner.RunAsync(() => sourceRepository.GetRecordedSessionSourceSnapshotAsync(e.SessionId));
        if (snapshot is null)
            await sourceStore.PublishSourcesRemovedAsync([e.SessionId]);
        else
            await sourceStore.PublishSourcesChangedAsync([snapshot]);
    }
}
