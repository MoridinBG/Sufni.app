using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.LiveStreaming;

// Per-tab transport client for the framed live preview protocol.
public interface ILiveDaqClient : IAsyncDisposable
{
    bool IsConnected { get; }

    // Emits parsed frames plus disconnect and fault notifications for this client.
    IObservable<LiveDaqClientEvent> Events { get; }

    // Attempt to connect to the remote DAQ
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    // Completes as Started only after the session header arrives, not merely after the
    // initial ACK accepts the request.
    Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default);

    // Waits for STOP_LIVE_ACK when a live session is active.
    Task StopPreviewAsync(CancellationToken cancellationToken = default);

    // Performs best-effort stop and socket teardown. Safe to call after the remote end
    // has already disconnected.
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}