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

    Task<SessionRecomputeResult> RecomputeAsync(
        Guid sessionId,
        long baselineUpdated,
        CancellationToken cancellationToken = default);

    Task<SessionDeleteResult> DeleteAsync(Guid sessionId);
}
