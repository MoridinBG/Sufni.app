using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public interface ILiveSessionService : IAsyncDisposable
{
    IObservable<LiveSessionPresentationSnapshot> Snapshots { get; }
    IObservable<LiveSignalBatch> SignalBatches { get; }
    LiveSessionPresentationSnapshot Current { get; }

    Task EnsureAttachedAsync(CancellationToken cancellationToken = default);
    Task ResetCaptureAsync(CancellationToken cancellationToken = default);
    Task<LiveSessionCapturePackage> PrepareCaptureForSaveAsync(CancellationToken cancellationToken = default);
}