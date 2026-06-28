using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;

namespace Sufni.App.Coordinators;

public interface ISessionCoordinator
{
    Task OpenEditAsync(Guid sessionId);

    Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default);

    Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated);

    Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default);

    Task<SessionRecomputeResult> RequestRecomputeAsync(Guid sessionId, RecomputeReason reason);

    /// <summary>
    /// True while a recompute for this session is in flight. The staleness
    /// prompter reads it to avoid prompting for a recompute the user just
    /// triggered (the request flips this true synchronously, before any await).
    /// </summary>
    bool IsRecomputeActive(Guid sessionId);

    Task<SessionDeleteResult> DeleteAsync(Guid sessionId);
}
