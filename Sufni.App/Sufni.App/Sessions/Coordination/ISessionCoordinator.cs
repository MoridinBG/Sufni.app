using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Coordination;

public interface ISessionCoordinator
{
    Task<SessionRecomputeResult> SetProcessingOptionAsync(Guid sessionId, int windowMilliseconds);

    Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated);

    Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default);

    Task<SessionDeleteResult> DeleteAsync(Guid sessionId);
}
