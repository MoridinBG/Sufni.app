using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;

using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Coordination;

/// <summary>
/// Runs the trim and split commands of recorded sessions. Each command commits
/// in one transaction, publishes the changed sessions, and
/// recomputes the affected sessions. A split reports its new id after publication.
/// </summary>
public interface ISessionEditingCoordinator
{
    Task StopAsync();

    Task<SessionEditResult> TrimUntilEndAsync(Guid sessionId, double clickSeconds);

    Task<SessionEditResult> TrimFromBeginningAsync(Guid sessionId, double clickSeconds);

    Task<SessionEditResult> UndoTrimAsync(Guid sessionId);

    Task<SessionEditResult> SplitAsync(Guid sessionId, double clickSeconds, IProgress<SessionEditProgress>? progress = null);
}

/// <summary>A split has published its new session; recomputation is still owned by the command.</summary>
public sealed record SessionEditProgress(Guid CreatedSessionId);

public abstract record SessionEditResult
{
    private SessionEditResult() { }

    /// <summary>
    /// The edit is committed. <paramref name="RecomputeFailure"/> is set when an
    /// affected session could not be recomputed afterwards.
    /// </summary>
    public sealed record Applied(SessionRecomputeResult? RecomputeFailure = null) : SessionEditResult;

    public sealed record Missing : SessionEditResult;
    public sealed record Failed(SessionEditFailureReason Reason, string? Details = null) : SessionEditResult;
}

internal sealed class SessionEditingCoordinator(
    ISessionStoreWriter sessionStore,
    ISessionPersistenceTransactionRunner transactions,
    ISessionRecomputeEngine recomputeEngine,
    ISessionRepository sessionRepository,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher dispatcher,
    StorePublication publication) : ISessionEditingCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SessionEditingCoordinator>();
    private readonly object activeTasksGate = new();
    private readonly HashSet<Task> activeTasks = [];

    public async Task StopAsync()
    {
        Task[] tasks;
        lock (activeTasksGate) tasks = activeTasks.ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task ReleaseAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (activeTasksGate) activeTasks.Remove(task);
    }


    public Task<SessionEditResult> TrimUntilEndAsync(Guid sessionId, double clickSeconds) =>
        RunAsync(sessionId, session => transactions.TrimUntilEndAsync(session, clickSeconds));

    public Task<SessionEditResult> TrimFromBeginningAsync(Guid sessionId, double clickSeconds) =>
        RunAsync(sessionId, session => transactions.TrimFromBeginningAsync(session, clickSeconds));

    public Task<SessionEditResult> UndoTrimAsync(Guid sessionId) =>
        RunAsync(sessionId, transactions.UndoTrimAsync);

    public Task<SessionEditResult> SplitAsync(Guid sessionId, double clickSeconds, IProgress<SessionEditProgress>? progress = null) =>
        RunAsync(sessionId, session => transactions.SplitAsync(session, clickSeconds), progress);

    private Task<SessionEditResult> RunAsync(Guid sessionId, Func<SessionSnapshot, Task<SessionEditCommit>> commit, IProgress<SessionEditProgress>? progress = null)
    {
        lock (activeTasksGate)
        {
            var task = RunCoreAsync(sessionId, commit, progress);
            activeTasks.Add(task);
            _ = ReleaseAsync(task);
            return task;
        }
    }

    private async Task<SessionEditResult> RunCoreAsync(
        Guid sessionId,
        Func<SessionSnapshot, Task<SessionEditCommit>> commit,
        IProgress<SessionEditProgress>? progress = null)
    {
        if (sessionStore.Get(sessionId) is not { } session)
        {
            return new SessionEditResult.Missing();
        }

        try
        {
            var outcome = await backgroundTaskRunner.RunAsync(() => commit(session));
            if (outcome is SessionEditCommit.Rejected rejected)
            {
                return new SessionEditResult.Failed(SessionEditFailureReason.Rejected, rejected.Message);
            }

            var committed = (SessionEditCommit.Committed)outcome;

            var snapshots = await backgroundTaskRunner.RunAsync(async () =>
            {
                var rows = await Task.WhenAll(committed.SessionIds.Select(sessionRepository.GetSessionAsync));
                return rows.OfType<Sufni.App.Sessions.Models.Session>().Select(SessionSnapshot.From).ToArray();
            });
            var removedIds = committed.SessionIds.Except(snapshots.Select(snapshot => snapshot.Id)).ToArray();
            await dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                if (removedIds.Length > 0) await sessionStore.PublishSessionsRemovedAsync(removedIds);
                await sessionStore.PublishSessionsChangedAsync(snapshots);
            });
            // Requested before any other await: the publish queues a projection
            // update, and an open editor prompts for a recompute of a stale session
            // unless one is already running.
            var recomputes = committed.SessionIds
                .Select(id => recomputeEngine.RequestRecomputeAsync(id))
                .ToArray();

            if (committed.CreatedSessionId is { } createdId)
            {
                progress?.Report(new SessionEditProgress(createdId));
            }

            var results = await Task.WhenAll(recomputes);
            return new SessionEditResult.Applied(DescribeRecomputeFailure(results));
        }
        catch (Exception e)
        {
            logger.Error(e, "Session edit failed for {SessionId}", sessionId);
            return new SessionEditResult.Failed(SessionEditFailureReason.Edit, e.Message);
        }
    }

    private static SessionRecomputeResult? DescribeRecomputeFailure(SessionRecomputeResult[] results) =>
        results.FirstOrDefault(result => result is not (SessionRecomputeResult.Recomputed or SessionRecomputeResult.Superseded));
}
