using System;
using System.Collections.Generic;
using System.IO;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;

using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

public enum SessionDataApplyResult { Applied, Superseded, Missing, InvalidPayload, Failed }

public interface ISessionSyncApplier
{
    Task<SessionDataApplyResult> ApplyUploadedAsync(Guid sessionId, SessionBlobPayload payload);
    Task<SessionDataApplyResult> ApplyDownloadedAsync(SessionSnapshot expected, SessionBlobPayload payload,
        string? targetFingerprint, SessionProcessedGeneration? generation = null);
}

internal sealed class SessionSyncApplier : ISessionSyncApplier, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<SessionSyncApplier>();

    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly IRecordedSessionSourceRepository sourceRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionStoreWriter sessionStore;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly ISessionTelemetryProcessor telemetryProcessor;
    private readonly ISessionBlobSwapRequestStore swapRequests;
    private ISynchronizationServerService? server;

    public SessionSyncApplier(
        ISessionStoreWriter sessionStore,
        IRecordedSessionSourceStoreWriter sourceStore,
        IUiThreadDispatcher uiThreadDispatcher,
        IRecordedSessionSourceRepository sourceRepository,
        ISessionRepository sessionRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionTelemetryProcessor telemetryProcessor,
        ISessionBlobSwapRequestStore swapRequests)
    {
        this.sessionStore = sessionStore;
        this.sourceStore = sourceStore;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.sourceRepository = sourceRepository;
        this.sessionRepository = sessionRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;

        this.telemetryProcessor = telemetryProcessor;
        this.swapRequests = swapRequests;
    }

    /// <summary>Connects desktop arrival notifications during startup, before the server starts.</summary>
    public void AttachServer(ISynchronizationServerService synchronizationServer)
    {
        server = synchronizationServer;
        server.SynchronizationDataArrived += OnSynchronizationDataArrived;
        server.SessionSourceDataArrived += OnSessionSourceDataArrived;
    }

    public void Dispose()
    {
        if (server is null) return;
        server.SynchronizationDataArrived -= OnSynchronizationDataArrived;
        server.SessionSourceDataArrived -= OnSessionSourceDataArrived;
        server = null;
    }

    public Task<SessionDataApplyResult> ApplyUploadedAsync(Guid sessionId, SessionBlobPayload payload) =>
        ApplyTransferAsync(sessionId, async () =>
        {
            var row = await sessionRepository.GetSessionAsync(sessionId);
            if (row is null) return SessionDataApplyResult.Missing;
            var swap = await swapRequests.GetRequestAsync(sessionId);
            var target = swap?.TargetFingerprint ?? row.ProcessingFingerprintJson;
            if (!StringComparer.Ordinal.Equals(payload.Fingerprint, target))
            {
                if (swap is not null) return SessionDataApplyResult.Superseded;
                logger.Warning("Uploaded session data does not match the requested fingerprint for {SessionId}", sessionId);
                return SessionDataApplyResult.InvalidPayload;
            }
            return await CommitTransferAsync(SessionSnapshot.From(row), payload, swap?.TargetGeneration, swap?.TargetFingerprint);
        });

    public Task<SessionDataApplyResult> ApplyDownloadedAsync(SessionSnapshot expected, SessionBlobPayload payload,
        string? targetFingerprint, SessionProcessedGeneration? generation = null) =>
        ApplyTransferAsync(expected.Id, () => StringComparer.Ordinal.Equals(payload.Fingerprint, targetFingerprint)
            ? CommitTransferAsync(expected, payload, generation, pendingSwapFingerprint: null)
            : Task.FromResult(SessionDataApplyResult.Superseded));

    private async Task<SessionDataApplyResult> ApplyTransferAsync(Guid sessionId, Func<Task<SessionDataApplyResult>> operation)
    {
        try
        {
            return await backgroundTaskRunner.RunAsync(operation);
        }
        catch (InvalidDataException exception)
        {
            logger.Warning(exception, "Incoming session data is invalid for {SessionId}", sessionId);
            return SessionDataApplyResult.InvalidPayload;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error(exception, "Incoming session data could not be applied for {SessionId}", sessionId);
            return SessionDataApplyResult.Failed;
        }
    }

    private async Task<SessionDataApplyResult> CommitTransferAsync(SessionSnapshot expected, SessionBlobPayload payload,
        SessionProcessedGeneration? generation, string? pendingSwapFingerprint)
    {
        var current = await sessionRepository.GetSessionAsync(expected.Id);
        if (current is null) return SessionDataApplyResult.Missing;
        var telemetry = telemetryProcessor.ReadProcessedTelemetryData(payload.Data);
        var guard = SessionProcessedWriteGuard.From(expected, pendingSwapFingerprint);
        bool committed;
        if (generation is not null)
        {
            committed = await sessionRepository.UpdateSessionProcessedGenerationAsync(
                expected.Id, payload.Data, payload.Fingerprint, generation, guard);
        }
        else
        {
            var duration = SessionDuration.Normalize(telemetry.Metadata?.Duration ?? current.DurationSeconds);
            committed = await sessionRepository.UpdateSessionPsstAsync(expected.Id, payload.Data, payload.Fingerprint, duration, guard);
        }
        if (!committed) return SessionDataApplyResult.Superseded;
        var fresh = await sessionRepository.GetSessionAsync(expected.Id);
        if (fresh is null)
        {
            await sessionStore.PublishSessionsRemovedAsync([expected.Id]);
            return SessionDataApplyResult.Superseded;
        }
        await sessionStore.PublishSessionsChangedAsync([SessionSnapshot.From(fresh)]);
        return SessionDataApplyResult.Applied;
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
            await PublishSessionsAsync(changes);
        }
    }

    private async Task PublishSessionsAsync(IReadOnlyCollection<Guid> ids)
    {
        var (snapshots, removed) = await backgroundTaskRunner.RunAsync(async () =>
        {
            var snapshots = new List<SessionSnapshot>();
            var removed = new List<Guid>();
            foreach (var id in ids)
            {
                var row = await sessionRepository.GetSessionAsync(id);
                if (row is null) removed.Add(id);
                else snapshots.Add(SessionSnapshot.From(row));
            }
            return (snapshots, removed);
        });
        if (removed.Count > 0) await sessionStore.PublishSessionsRemovedAsync(removed);
        if (snapshots.Count > 0) await sessionStore.PublishSessionsChangedAsync(snapshots);
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
