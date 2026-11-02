using System.Threading;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Store;
using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Coordinators;

public interface ILiveDaqCoordinator
{
    Task<LiveSessionSaveResult> SaveCaptureAsync(SessionSnapshot draft, ILiveSessionService captureService,
        SessionPreferences preferences, CancellationToken cancellationToken = default);

    void Activate();

    void Deactivate();

}
