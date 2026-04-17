using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveSessionService : IAsyncDisposable
{
    IObservable<LiveSessionPresentationSnapshot> Snapshots { get; }
    IObservable<LiveGraphBatch> GraphBatches { get; }
    LiveSessionPresentationSnapshot Current { get; }

    Task EnsureAttachedAsync(CancellationToken cancellationToken = default);
    Task ResetCaptureAsync(CancellationToken cancellationToken = default);
    Task<LiveSessionCapturePackage> PrepareCaptureForSaveAsync(CancellationToken cancellationToken = default);
}