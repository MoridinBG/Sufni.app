using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.Telemetry;
namespace Sufni.App.Sessions.Coordination;

public interface ISessionCoordinator
{
    Task<SessionDetailLoadResult> LoadDetailAsync(
        Guid sessionId,
        IProgress<SessionDetailLoadProgress> progress,
        CancellationToken cancellationToken = default);

    Task<SessionDetailTrackLoadResult> LoadTrackAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        IProgress<SessionDetailLoadProgress> progress,
        CancellationToken cancellationToken = default);

    Task<SessionRecomputeResult> SetProcessingOptionAsync(Guid sessionId, int windowMilliseconds);

    Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated);

    Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default);

    Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason);

    /// <summary>
    /// Rebuilds every recomputable recorded session in parallel, scaled to the
    /// available hardware. Sessions that cannot be recomputed are skipped. When
    /// supplied, <paramref name="progress"/> is reported as sessions finish.
    /// </summary>
    Task<SessionRecomputeAllResult> RequestRecomputeAllAsync(
        IProgress<SessionRecomputeAllProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True while a recompute for this session is in flight. The staleness
    /// prompter reads it to avoid prompting for a recompute the user just
    /// triggered (the request flips this true synchronously, before any await).
    /// </summary>
    bool IsRecomputeActive(Guid sessionId);

    Task<SessionDeleteResult> DeleteAsync(Guid sessionId);
}
