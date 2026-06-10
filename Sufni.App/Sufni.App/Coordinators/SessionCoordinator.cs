using Sufni.App.ExtensionHost.SessionGraph;

using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns recorded-session workflows.
/// It opens session detail state, loads desktop and mobile telemetry, saves
/// metadata and live captures, recomputes derived data, deletes sessions, and
/// applies inbound session changes.
/// </summary>
public class SessionCoordinator : ISessionCoordinator
{
    private readonly ISessionStoreWriter sessionStore;
    private readonly SessionLoader sessionLoader;
    private readonly SessionSaver sessionSaver;
    private readonly LiveCaptureSaver liveCaptureSaver;
    private readonly SessionRecomputer sessionRecomputer;
    private readonly SessionDeleter sessionDeleter;
    private readonly Func<IEditorFactory> editorFactory;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        SessionLoader sessionLoader,
        SessionSaver sessionSaver,
        LiveCaptureSaver liveCaptureSaver,
        SessionRecomputer sessionRecomputer,
        SessionDeleter sessionDeleter,
        Func<IEditorFactory> editorFactory)
    {
        this.sessionStore = sessionStore;
        this.sessionLoader = sessionLoader;
        this.sessionSaver = sessionSaver;
        this.liveCaptureSaver = liveCaptureSaver;
        this.sessionRecomputer = sessionRecomputer;
        this.sessionDeleter = sessionDeleter;
        this.editorFactory = editorFactory;
    }

    public virtual Task OpenEditAsync(Guid sessionId)
    {
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null) return Task.CompletedTask;

        editorFactory().OpenSessionDetail(snapshot);
        return Task.CompletedTask;
    }

    public virtual Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadDesktopDetailAsync(sessionId, cancellationToken);

    public virtual Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadMobileDetailAsync(sessionId, dimensions, cancellationToken);

    public virtual Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated) =>
        sessionSaver.SaveAsync(session, baselineUpdated);

    public virtual Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
        => liveCaptureSaver.SaveLiveCaptureAsync(session, capture, preferences, cancellationToken);

    public virtual Task<SessionRecomputeResult> RecomputeAsync(
        Guid sessionId,
        long baselineUpdated,
        CancellationToken cancellationToken = default)
        => sessionRecomputer.RecomputeAsync(sessionId, baselineUpdated, cancellationToken);

    public virtual Task<SessionDeleteResult> DeleteAsync(Guid sessionId) =>
        sessionDeleter.DeleteAsync(sessionId);
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
