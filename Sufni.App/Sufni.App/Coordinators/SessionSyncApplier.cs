using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public sealed class SessionSyncApplier
{
    private static readonly ILogger logger = Log.ForContext<SessionSyncApplier>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    public SessionSyncApplier(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IUiThreadDispatcher uiThreadDispatcher,
        ISynchronizationServerService? synchronizationServer = null)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.sourceStore = sourceStore;
        this.uiThreadDispatcher = uiThreadDispatcher;

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

            var fresh = await sessionRepository.GetSessionAsync(session.Id);
            if (fresh is not null)
            {
                upserts.Add(SessionSnapshot.From(fresh));
            }
        }

        logger.Verbose(
            "Applying inbound session synchronization with {RemovalCount} removals and {UpsertCount} upserts",
            removals.Count,
            upserts.Count);

        await uiThreadDispatcher.InvokeAsync(() =>
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

        var fresh = await sessionRepository.GetSessionAsync(e.SessionId);
        if (fresh is null)
        {
            logger.Verbose("Ignoring inbound session data because session {SessionId} is missing", e.SessionId);
            return;
        }

        var snapshot = SessionSnapshot.From(fresh);
        await uiThreadDispatcher.InvokeAsync(() =>
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

        var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(e.SessionId);
        if (source is null)
        {
            logger.Verbose("Ignoring inbound recorded source because source {SessionId} is missing", e.SessionId);
            return;
        }

        var snapshot = RecordedSessionSourceSnapshot.From(source);
        await uiThreadDispatcher.InvokeAsync(() =>
        {
            sourceStore.Upsert(snapshot);
        });
    }
}
