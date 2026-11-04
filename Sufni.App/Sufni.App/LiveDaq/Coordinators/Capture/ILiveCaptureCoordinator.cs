using System.Threading.Tasks;
using System.Threading;
using System;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

public interface ILiveCaptureCoordinator : IAsyncDisposable
{
    IObservable<LiveCaptureState> States { get; }
    IObservable<LiveSignalBatch> SignalBatches { get; }
    LiveCaptureState Current { get; }

    Task<LivePreviewStartResult?> AttachAsync(CancellationToken cancellationToken = default);
    void UpdateAnalysisOptions(LiveAnalysisOptions options);
    Task ResetAsync(CancellationToken cancellationToken = default);
    Task<LiveSessionCapturePackage> PrepareForSaveAsync(CancellationToken cancellationToken = default);
}
