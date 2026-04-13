using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqSharedStream : IAsyncDisposable
{
    string IdentityKey { get; }
    LiveDaqStreamConfiguration RequestedConfiguration { get; }
    LiveDaqSharedStreamState CurrentState { get; }

    IObservable<LiveProtocolFrame> Frames { get; }
    IObservable<LiveDaqSharedStreamState> States { get; }

    Task AttachDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task DetachDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task AttachSessionAsync(CancellationToken cancellationToken = default);
    Task DetachSessionAsync(CancellationToken cancellationToken = default);

    Task<LivePreviewStartResult?> EnsureStartedAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task ApplyConfigurationAsync(LiveDaqStreamConfiguration configuration, CancellationToken cancellationToken = default);
}