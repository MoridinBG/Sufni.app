using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Sessions.Coordination;

/// <summary>
/// VM-facing entry point for recorded-session workflows. A thin router over
/// <see cref="SessionLoader"/> (pure reads) and <see cref="SessionCommandService"/>
/// (store-writing commands and recompute requests).
/// </summary>
public class SessionCoordinator : ISessionCoordinator
{
    private readonly ISessionStoreWriter sessionStore;
    private readonly SessionLoader sessionLoader;
    private readonly SessionCommandService commandService;
    private readonly Func<IEditorFactory> editorFactory;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        SessionLoader sessionLoader,
        SessionCommandService commandService,
        Func<IEditorFactory> editorFactory)
    {
        this.sessionStore = sessionStore;
        this.sessionLoader = sessionLoader;
        this.commandService = commandService;
        this.editorFactory = editorFactory;
    }

    public Task OpenEditAsync(Guid sessionId)
    {
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null) return Task.CompletedTask;

        editorFactory().OpenSessionDetail(snapshot);
        return Task.CompletedTask;
    }

    public Task<SessionDetailLoadResult> LoadDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadDetailAsync(sessionId, dimensions, cancellationToken);

    public Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated) =>
        commandService.SaveAsync(session, baselineUpdated);

    public Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
        => commandService.SaveLiveCaptureAsync(session, capture, preferences, cancellationToken);

    public Task<Guid?> CreateDerivedSessionAsync(
        Guid fromSessionId,
        string name,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default) =>
        commandService.CreateDerivedSessionAsync(fromSessionId, name, sourceAbsoluteStartSeconds, cancellationToken);

    public Task<bool> UpdateSessionOriginAsync(
        Guid sessionId,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default) =>
        commandService.UpdateSessionOriginAsync(sessionId, sourceAbsoluteStartSeconds, cancellationToken);

    public Task<bool> RenameSessionAsync(
        Guid sessionId,
        string name,
        CancellationToken cancellationToken = default) =>
        commandService.RenameSessionAsync(sessionId, name, cancellationToken);

    public Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason) =>
        commandService.RequestRecomputeAsync(sessionId, reason);

    public Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        IProgress<SessionRecomputeAllProgress>? progress = null) =>
        commandService.RequestRecomputeAllAsync(RecomputeReason.RecomputeAll, progress);

    public bool IsRecomputeActive(Guid sessionId) => commandService.IsRecomputeActive(sessionId);

    public Task<SessionDeleteResult> DeleteAsync(Guid sessionId) =>
        commandService.DeleteAsync(sessionId);
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
/// It distinguishes successful recompute, displacement by a newer explicit
/// request, unrecomputable current state, and failure.
/// </summary>
public abstract record SessionRecomputeResult
{
    private SessionRecomputeResult() { }

    public sealed record Recomputed(long NewBaselineUpdated) : SessionRecomputeResult;

    /// <summary>
    /// A newer explicit recompute request displaced this run before it
    /// committed. It is terminal-no-op for the caller: the newer run owns the result,
    /// and the caller never advances a baseline or clears dirty from it.
    /// </summary>
    public sealed record Superseded : SessionRecomputeResult;
    public sealed record NotRecomputable(SessionStaleness Reason) : SessionRecomputeResult;
    public sealed record Failed(string ErrorMessage) : SessionRecomputeResult;
}

/// <summary>
/// Aggregate outcome of a recompute-all request. The counts are disjoint and
/// sum to <see cref="Total"/>, the number of live sessions considered.
/// </summary>
public sealed record SessionRecomputeAllResult(
    int Total,
    int Recomputed,
    int Superseded,
    int NotRecomputable,
    int Failed);

/// <summary>
/// Incremental progress for a recompute-all run: how many of the considered
/// sessions have finished so far.
/// </summary>
public sealed record SessionRecomputeAllProgress(int Completed, int Total);

public sealed record SessionDeleteResult(SessionDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SessionDeleteOutcome
{
    Deleted,
    Failed
}
