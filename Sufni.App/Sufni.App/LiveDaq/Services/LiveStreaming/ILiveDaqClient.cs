using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

// Transport client for one framed live preview connection, typically owned by a shared stream.
public interface ILiveDaqClient : IAsyncDisposable
{
    bool IsConnected { get; }

    // Emits session data plus typed lifecycle outcomes; wire acknowledgements stay inside the client.
    IObservable<LiveDaqClientEvent> Events { get; }

    // Attempt to connect to the remote DAQ
    Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);

    // Completes as Started only after the session header arrives, not merely after the
    // initial ACK accepts the request.
    Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default);

    // Waits for STOP_LIVE_ACK when a live session is active.
    Task StopPreviewAsync(CancellationToken cancellationToken = default);

    // Performs best-effort stop and socket teardown. Safe to call after the remote end
    // has already disconnected.
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
