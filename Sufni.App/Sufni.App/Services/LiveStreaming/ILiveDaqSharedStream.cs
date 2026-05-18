using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Stores;

namespace Sufni.App.Services.LiveStreaming;

// Lease held by a consumer while it is attached to the shared DAQ connection.
// Disposing the last lease lets the stream stop and evict itself.
public interface ILiveDaqSharedStreamLease : IAsyncDisposable
{
}

// Shared live connection for one DAQ identity. Multiple tabs/services can
// consume the same frame stream while configuration changes are serialized.
public interface ILiveDaqSharedStream : IAsyncDisposable
{
    string IdentityKey { get; }
    LiveDaqSnapshot CatalogSnapshot { get; }
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
