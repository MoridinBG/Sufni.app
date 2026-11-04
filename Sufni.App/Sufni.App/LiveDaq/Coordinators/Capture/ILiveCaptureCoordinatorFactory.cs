using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

public interface ILiveCaptureCoordinatorFactory
{
    ILiveCaptureCoordinator Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream);
}
