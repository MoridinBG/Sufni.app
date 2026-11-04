using System;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Services;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed class LiveCaptureCoordinatorFactory(
    ISessionPresentationService sessionPresentationService,
    IBackgroundTaskRunner backgroundTaskRunner,
    LiveSignalPipelineFactory liveSignalPipelineFactory,
    TimeProvider timeProvider,
    IUiThreadDispatcher uiThreadDispatcher) : ILiveCaptureCoordinatorFactory
{
    public ILiveCaptureCoordinator Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream)
    {
        return new LiveCaptureCoordinator(
            context,
            sharedStream,
            sessionPresentationService,
            backgroundTaskRunner,
            liveSignalPipelineFactory.Create(),
            timeProvider,
            uiThreadDispatcher);
    }
}
