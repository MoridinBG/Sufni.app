using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqClient : IAsyncDisposable
{
    bool IsConnected { get; }

    IObservable<LiveDaqClientEvent> Events { get; }

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default);

    Task StopPreviewAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}