using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveDaqSharedStream : ILiveDaqSharedStream
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqSharedStream>();

    private readonly ILiveDaqClientFactory liveDaqClientFactory;
    private readonly Func<LiveDaqSharedStream, Task> evictAsync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Subject<LiveProtocolFrame> framesSubject = new();
    private readonly BehaviorSubject<LiveDaqSharedStreamState> statesSubject = new(LiveDaqSharedStreamState.Empty);
    private readonly EventLoopScheduler clientEventScheduler = new();

    private LiveDaqSnapshot snapshot;
    private ILiveDaqClient? liveDaqClient;
    private IDisposable? liveDaqClientSubscription;
    private LiveDaqStreamConfiguration requestedConfiguration = LiveDaqStreamConfiguration.Default;
    private LiveDaqSharedStreamState currentState = LiveDaqSharedStreamState.Empty;
    private int observerCount;
    private int configurationLockCount;
    private int pendingDeliberateDisconnectCount;
    private bool isEvictionPending;
    private bool isDisposed;
    private bool isEvicted;

    public LiveDaqSharedStream(
        LiveDaqSnapshot snapshot,
        ILiveDaqClientFactory liveDaqClientFactory,
        Func<LiveDaqSharedStream, Task> evictAsync)
    {
        this.snapshot = snapshot;
        this.liveDaqClientFactory = liveDaqClientFactory;
        this.evictAsync = evictAsync;
    }

    public string IdentityKey => snapshot.IdentityKey;

    public LiveDaqStreamConfiguration RequestedConfiguration => requestedConfiguration;

    public LiveDaqSharedStreamState CurrentState => currentState;

    public IObservable<LiveProtocolFrame> Frames => framesSubject.AsObservable();

    public IObservable<LiveDaqSharedStreamState> States => statesSubject.AsObservable();

    public ILiveDaqSharedStreamLease AcquireLease()
    {
        return AcquireLease(releaseConfigurationLock: false);
    }

    public ILiveDaqSharedStreamLease AcquireConfigurationLock()
    {
        return AcquireLease(releaseConfigurationLock: true);
    }

    public async Task<LivePreviewStartResult?> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        LivePreviewStartResult? startResult = null;

        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (currentState.IsClosed || currentState.ConnectionState is LiveConnectionState.Connecting or LiveConnectionState.Connected)
            {
                return null;
            }

            if (!TryGetEndpoint(out var host, out var port))
            {
                PublishState(currentState with
                {
                    ConnectionState = LiveConnectionState.Disconnected,
                    LastError = "DAQ is offline.",
                    SessionHeader = null,
                    SelectedSensorMask = LiveSensorMask.None,
                });
                return new LivePreviewStartResult.Failed("DAQ is offline.");
            }

            logger.Information(
                "Starting shared live DAQ stream for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Connecting,
                LastError = null,
                SessionHeader = null,
                SelectedSensorMask = LiveSensorMask.None,
            });

            var client = EnsureClientCreated();
            if (!client.IsConnected)
            {
                await client.ConnectAsync(host, port, cancellationToken);
            }

            startResult = await client.StartPreviewAsync(requestedConfiguration.ToStartRequest(), cancellationToken);
            switch (startResult)
            {
                case LivePreviewStartResult.Started started:
                    logger.Information(
                        "Shared live DAQ stream connected for {IdentityKey} at {Endpoint} with session {SessionId}",
                        IdentityKey,
                        snapshot.Endpoint,
                        started.Header.SessionId);
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Connected,
                        LastError = null,
                        SessionHeader = started.Header,
                        SelectedSensorMask = started.SelectedSensorMask,
                    });
                    break;

                case LivePreviewStartResult.Rejected rejected:
                    logger.Warning(
                        "Shared live DAQ stream rejected for {IdentityKey} at {Endpoint}: {ErrorCode} {ErrorMessage}",
                        IdentityKey,
                        snapshot.Endpoint,
                        rejected.ErrorCode,
                        rejected.UserMessage);
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnected,
                        LastError = rejected.UserMessage,
                        SessionHeader = null,
                        SelectedSensorMask = LiveSensorMask.None,
                    });
                    BeginDeliberateDisconnect();
                    await client.DisconnectAsync(cancellationToken);
                    break;

                case LivePreviewStartResult.Failed failed:
                    logger.Error(
                        "Shared live DAQ stream failed for {IdentityKey} at {Endpoint}: {ErrorMessage}",
                        IdentityKey,
                        snapshot.Endpoint,
                        failed.ErrorMessage);
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnected,
                        LastError = failed.ErrorMessage,
                        SessionHeader = null,
                        SelectedSensorMask = LiveSensorMask.None,
                    });
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Starting shared live DAQ stream failed for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);
            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = ex.Message,
                SessionHeader = null,
                SelectedSensorMask = LiveSensorMask.None,
            });
            startResult = new LivePreviewStartResult.Failed(ex.Message);
        }
        finally
        {
            gate.Release();
        }

        return startResult;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (currentState.IsClosed)
            {
                return;
            }

            if (liveDaqClient is null)
            {
                PublishState(currentState with
                {
                    ConnectionState = LiveConnectionState.Disconnected,
                    LastError = null,
                    SessionHeader = null,
                    SelectedSensorMask = LiveSensorMask.None,
                });
                return;
            }

            if (!liveDaqClient.IsConnected && currentState.ConnectionState is LiveConnectionState.Disconnected)
            {
                PublishState(currentState with
                {
                    LastError = null,
                    SessionHeader = null,
                    SelectedSensorMask = LiveSensorMask.None,
                });
                return;
            }

            logger.Information(
                "Stopping shared live DAQ stream for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnecting,
                LastError = null,
            });

            BeginDeliberateDisconnect();
            await liveDaqClient.DisconnectAsync(cancellationToken);
            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = null,
                SessionHeader = null,
                SelectedSensorMask = LiveSensorMask.None,
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Stopping shared live DAQ stream failed for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);
            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = ex.Message,
                SessionHeader = null,
                SelectedSensorMask = LiveSensorMask.None,
            });
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ApplyConfigurationAsync(LiveDaqStreamConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (currentState.IsClosed || currentState.IsConfigurationLocked)
            {
                return;
            }

            if (requestedConfiguration == configuration)
            {
                return;
            }

            requestedConfiguration = configuration;
            if (currentState.ConnectionState is not LiveConnectionState.Connected)
            {
                return;
            }

            logger.Information(
                "Reconfiguring shared live DAQ stream for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnecting,
                LastError = null,
            });

            BeginDeliberateDisconnect();
            await liveDaqClient!.DisconnectAsync(cancellationToken);
            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = null,
                SessionHeader = null,
                SelectedSensorMask = LiveSensorMask.None,
            });

            if (!TryGetEndpoint(out var host, out var port))
            {
                PublishState(currentState with
                {
                    LastError = "DAQ is offline.",
                });
                return;
            }

            var client = EnsureClientCreated();
            if (!client.IsConnected)
            {
                await client.ConnectAsync(host, port, cancellationToken);
            }

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Connecting,
                LastError = null,
            });

            var result = await client.StartPreviewAsync(requestedConfiguration.ToStartRequest(), cancellationToken);
            switch (result)
            {
                case LivePreviewStartResult.Started started:
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Connected,
                        LastError = null,
                        SessionHeader = started.Header,
                        SelectedSensorMask = started.SelectedSensorMask,
                    });
                    break;

                case LivePreviewStartResult.Rejected rejected:
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnected,
                        LastError = rejected.UserMessage,
                        SessionHeader = null,
                        SelectedSensorMask = LiveSensorMask.None,
                    });
                    BeginDeliberateDisconnect();
                    await client.DisconnectAsync(cancellationToken);
                    break;

                case LivePreviewStartResult.Failed failed:
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnected,
                        LastError = failed.ErrorMessage,
                        SessionHeader = null,
                        SelectedSensorMask = LiveSensorMask.None,
                    });
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Reconfiguring shared live DAQ stream failed for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);
            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = ex.Message,
                SessionHeader = null,
                SelectedSensorMask = LiveSensorMask.None,
            });
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateCatalogSnapshotAsync(LiveDaqSnapshot nextSnapshot, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (isDisposed || currentState.IsClosed)
            {
                return;
            }

            snapshot = nextSnapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CloseAsync(string errorMessage, CancellationToken cancellationToken = default)
    {
        var shouldEvict = false;

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (isDisposed || currentState.IsClosed)
            {
                return;
            }

            logger.Warning(
                "Closing shared live DAQ stream for {IdentityKey} at {Endpoint}: {ErrorMessage}",
                IdentityKey,
                snapshot.Endpoint,
                errorMessage);

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = errorMessage,
                IsClosed = true,
            });

            isEvictionPending = true;
            await DisposeClientAsync(cancellationToken);
            shouldEvict = true;
        }
        finally
        {
            gate.Release();
        }

        if (shouldEvict)
        {
            await EvictAsync();
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            await DisposeClientAsync(CancellationToken.None).ConfigureAwait(false);
            statesSubject.OnCompleted();
            framesSubject.OnCompleted();
            statesSubject.Dispose();
            framesSubject.Dispose();
            clientEventScheduler.Dispose();
        }
        finally
        {
            gate.Release();
        }

        gate.Dispose();
    }

    private ILiveDaqSharedStreamLease AcquireLease(bool releaseConfigurationLock)
    {
        gate.Wait();
        try
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(isEvictionPending || currentState.IsClosed, this);
            observerCount++;
            if (releaseConfigurationLock)
            {
                configurationLockCount++;
            }

            PublishState(currentState);
            return new LiveDaqSharedStreamLease(this, releaseConfigurationLock);
        }
        finally
        {
            gate.Release();
        }
    }

    internal ILiveDaqSharedStreamLease? TryAcquireReservationLease()
    {
        gate.Wait();
        try
        {
            if (isDisposed || isEvictionPending || currentState.IsClosed)
            {
                return null;
            }

            observerCount++;
            PublishState(currentState);
            return new LiveDaqSharedStreamLease(this, releaseConfigurationLock: false);
        }
        finally
        {
            gate.Release();
        }
    }

    private ILiveDaqClient EnsureClientCreated()
    {
        if (liveDaqClient is not null)
        {
            return liveDaqClient;
        }

        liveDaqClient = liveDaqClientFactory.CreateClient();
        liveDaqClientSubscription = liveDaqClient.Events
            .ObserveOn(clientEventScheduler)
            .Subscribe(clientEvent => _ = HandleClientEventAsync(clientEvent));
        return liveDaqClient;
    }

    private async Task DisposeClientAsync(CancellationToken cancellationToken)
    {
        if (liveDaqClient is null)
        {
            return;
        }

        var client = liveDaqClient;
        try
        {
            if (client.IsConnected)
            {
                BeginDeliberateDisconnect();
            }

            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Debug(
                ex,
                "Disposing shared live DAQ client failed for {IdentityKey} at {Endpoint}",
                IdentityKey,
                snapshot.Endpoint);
        }
        finally
        {
            liveDaqClientSubscription?.Dispose();
            liveDaqClientSubscription = null;
            liveDaqClient = null;
        }
    }

    private async Task HandleClientEventAsync(LiveDaqClientEvent clientEvent)
    {
        string? closeError = null;
        try
        {
            await gate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (isDisposed)
            {
                return;
            }

            switch (clientEvent)
            {
                case LiveDaqClientEvent.FrameReceived frameReceived:
                    if (frameReceived.Frame is LiveErrorFrame errorFrame)
                    {
                        PublishState(currentState with
                        {
                            ConnectionState = LiveConnectionState.Disconnected,
                            LastError = errorFrame.Payload.ErrorCode.ToUserMessage(),
                            SessionHeader = null,
                            SelectedSensorMask = LiveSensorMask.None,
                        });
                    }

                    framesSubject.OnNext(frameReceived.Frame);

                    break;

                case LiveDaqClientEvent.Faulted faulted:
                    closeError = faulted.ErrorMessage;
                    break;

                case LiveDaqClientEvent.Disconnected disconnected:
                    if (TryConsumeDeliberateDisconnect())
                    {
                        break;
                    }

                    closeError = disconnected.ErrorMessage ?? "Live preview disconnected unexpectedly.";
                    break;
            }
        }
        finally
        {
            gate.Release();
        }

        if (!string.IsNullOrWhiteSpace(closeError))
        {
            await CloseAsync(closeError, CancellationToken.None);
        }
    }

    private void PublishState(LiveDaqSharedStreamState nextState)
    {
        currentState = nextState with
        {
            IsConfigurationLocked = configurationLockCount > 0,
        };
        statesSubject.OnNext(currentState);
    }

    private async ValueTask ReleaseLeaseAsync(bool releaseConfigurationLock)
    {
        var shouldEvict = false;

        try
        {
            await gate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (isDisposed)
            {
                return;
            }

            observerCount = Math.Max(0, observerCount - 1);
            if (releaseConfigurationLock)
            {
                configurationLockCount = Math.Max(0, configurationLockCount - 1);
            }

            PublishState(currentState);

            if (observerCount != 0)
            {
                return;
            }

            isEvictionPending = true;
            await DisposeClientAsync(CancellationToken.None);
            shouldEvict = true;
        }
        finally
        {
            gate.Release();
        }

        if (shouldEvict)
        {
            await EvictAsync();
            await DisposeAsync();
        }
    }

    private bool TryGetEndpoint(out string host, out int port)
    {
        host = snapshot.Host ?? string.Empty;
        port = snapshot.Port ?? 0;
        return snapshot.IsOnline && !string.IsNullOrWhiteSpace(host) && snapshot.Port is not null;
    }

    private async Task EvictAsync()
    {
        if (isEvicted)
        {
            return;
        }

        isEvicted = true;
        await evictAsync(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private void BeginDeliberateDisconnect()
    {
        pendingDeliberateDisconnectCount++;
    }

    private bool TryConsumeDeliberateDisconnect()
    {
        if (pendingDeliberateDisconnectCount == 0)
        {
            return false;
        }

        pendingDeliberateDisconnectCount--;
        return true;
    }

    private sealed class LiveDaqSharedStreamLease : ILiveDaqSharedStreamLease
    {
        private readonly LiveDaqSharedStream owner;
        private readonly bool releaseConfigurationLock;
        private bool isDisposed;

        public LiveDaqSharedStreamLease(LiveDaqSharedStream owner, bool releaseConfigurationLock)
        {
            this.owner = owner;
            this.releaseConfigurationLock = releaseConfigurationLock;
        }

        public async ValueTask DisposeAsync()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            await owner.ReleaseLeaseAsync(releaseConfigurationLock);
        }
    }
}