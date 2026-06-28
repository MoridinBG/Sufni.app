using Sufni.App.ExtensionHost.Contracts.SessionGraph;

using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

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

    public Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadDesktopDetailAsync(sessionId, cancellationToken);

    public Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadMobileDetailAsync(sessionId, dimensions, cancellationToken);

    public Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated) =>
        commandService.SaveAsync(session, baselineUpdated);

    public Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
        => commandService.SaveLiveCaptureAsync(session, capture, preferences, cancellationToken);

    public Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason) =>
        commandService.RequestRecomputeAsync(sessionId, reason);

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

public sealed record SessionDeleteResult(SessionDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SessionDeleteOutcome
{
    Deleted,
    Failed
}
