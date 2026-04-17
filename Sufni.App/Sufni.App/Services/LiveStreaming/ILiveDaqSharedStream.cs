using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqSharedStreamLease : IAsyncDisposable
{
}

public interface ILiveDaqSharedStream : IAsyncDisposable
{
    string IdentityKey { get; }
    LiveDaqStreamConfiguration RequestedConfiguration { get; }
    LiveDaqSharedStreamState CurrentState { get; }

    IObservable<LiveProtocolFrame> Frames { get; }
    IObservable<LiveDaqSharedStreamState> States { get; }

    ILiveDaqSharedStreamLease AcquireLease();
    ILiveDaqSharedStreamLease AcquireConfigurationLock();

    Task<LivePreviewStartResult?> EnsureStartedAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task ApplyConfigurationAsync(LiveDaqStreamConfiguration configuration, CancellationToken cancellationToken = default);
}