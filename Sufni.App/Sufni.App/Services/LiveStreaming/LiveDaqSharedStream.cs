using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveDaqSharedStream : ILiveDaqSharedStream
{
    private const int FrameBufferCapacity = 1024;

    private static readonly ILogger logger = Log.ForContext<LiveDaqSharedStream>();

    private readonly Func<ILiveDaqClient> createLiveDaqClient;
    private readonly Func<LiveDaqSharedStream, Task> evictAsync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly BufferedFrameStream frames;
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
    private long evictionSequence;

    public LiveDaqSharedStream(
        LiveDaqSnapshot snapshot,
        Func<ILiveDaqClient> createLiveDaqClient,
        Func<LiveDaqSharedStream, Task> evictAsync)
    {
        this.snapshot = snapshot;
        this.createLiveDaqClient = createLiveDaqClient;
        this.evictAsync = evictAsync;
        frames = new BufferedFrameStream(FrameBufferCapacity);
    }

    public string IdentityKey => snapshot.IdentityKey;

    public LiveDaqSnapshot CatalogSnapshot => snapshot;

    public LiveDaqStreamConfiguration RequestedConfiguration => requestedConfiguration;

    public LiveDaqSharedStreamState CurrentState => currentState;

    public IObservable<LiveProtocolFrame> Frames => frames;

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
                        ClientDropCounters = LiveDaqClientDropCounters.Empty,
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
                        ClientDropCounters = LiveDaqClientDropCounters.Empty,
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
            frames.Complete();
            statesSubject.Dispose();
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
            ObjectDisposedException.ThrowIf(isEvicted || currentState.IsClosed, this);
            if (isEvictionPending)
            {
                isEvictionPending = false;
                evictionSequence++;
            }

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

    internal bool CanBeReturnedFromRegistry()
    {
        try
        {
            gate.Wait();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            return !isDisposed && !isEvicted && !isEvictionPending && !currentState.IsClosed;
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

        liveDaqClient = createLiveDaqClient();
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

                    var subscriberDroppedFrameCount = frames.Publish(frameReceived.Frame);
                    NoteSubscriberFrameDropsLocked(subscriberDroppedFrameCount);

                    break;

                case LiveDaqClientEvent.DropCountersChanged countersChanged:
                    PublishClientDropCountersLocked(countersChanged.Counters with
                    {
                        SubscriberFramesDropped = currentState.ClientDropCounters.SubscriberFramesDropped,
                    });
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
        if (nextState.ConnectionState is not LiveConnectionState.Connected || nextState.IsClosed)
        {
            frames.Reset();
        }

        currentState = nextState with
        {
            IsConfigurationLocked = configurationLockCount > 0,
        };
        statesSubject.OnNext(currentState);
    }

    private void NoteSubscriberFrameDropsLocked(int droppedFrameCount)
    {
        if (droppedFrameCount <= 0)
        {
            return;
        }

        PublishClientDropCountersLocked(currentState.ClientDropCounters.Add(
            LiveDaqClientDropCounters.Empty with
            {
                SubscriberFramesDropped = (ulong)droppedFrameCount,
            }));
    }

    private void PublishClientDropCountersLocked(LiveDaqClientDropCounters counters)
    {
        currentState = currentState with { ClientDropCounters = counters };
        statesSubject.OnNext(currentState);
    }

    private async ValueTask ReleaseLeaseAsync(bool releaseConfigurationLock)
    {
        var shouldBeginEviction = false;
        long currentEvictionSequence = 0;

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
            currentEvictionSequence = ++evictionSequence;
            shouldBeginEviction = true;
        }
        finally
        {
            gate.Release();
        }

        if (!shouldBeginEviction)
        {
            return;
        }

        await DisposeClientAsync(CancellationToken.None);

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

            shouldEvict = observerCount == 0
                && isEvictionPending
                && !currentState.IsClosed
                && currentEvictionSequence == evictionSequence;
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

    private sealed class BufferedFrameStream : IObservable<LiveProtocolFrame>
    {
        private readonly object gate = new();
        private readonly List<BufferedFrameSubscriber> subscribers = [];
        private readonly int capacity;
        private long epoch;
        private bool isCompleted;

        public BufferedFrameStream(int capacity)
        {
            this.capacity = capacity;
        }

        public IDisposable Subscribe(IObserver<LiveProtocolFrame> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            BufferedFrameSubscriber subscriber;
            lock (gate)
            {
                if (isCompleted)
                {
                    observer.OnCompleted();
                    return NoopDisposable.Instance;
                }

                subscriber = new BufferedFrameSubscriber(this, observer, capacity);
                subscribers.Add(subscriber);
            }

            subscriber.Start();
            return new FrameSubscription(this, subscriber);
        }

        public int Publish(LiveProtocolFrame frame)
        {
            BufferedFrameSubscriber[] snapshot;
            long frameEpoch;
            lock (gate)
            {
                if (isCompleted)
                {
                    return 0;
                }

                frameEpoch = epoch;
                snapshot = [.. subscribers];
            }

            var item = new BufferedFrameItem(frame, frameEpoch);
            var droppedFrameCount = 0;
            foreach (var subscriber in snapshot)
            {
                if (subscriber.Enqueue(item))
                {
                    droppedFrameCount++;
                }
            }

            return droppedFrameCount;
        }

        public void Reset()
        {
            lock (gate)
            {
                if (isCompleted)
                {
                    return;
                }

                epoch++;
            }
        }

        public void Complete()
        {
            BufferedFrameSubscriber[] snapshot;
            lock (gate)
            {
                if (isCompleted)
                {
                    return;
                }

                isCompleted = true;
                epoch++;
                snapshot = [.. subscribers];
                subscribers.Clear();
            }

            foreach (var subscriber in snapshot)
            {
                subscriber.CompleteFromSource();
            }
        }

        private void Unsubscribe(BufferedFrameSubscriber subscriber)
        {
            lock (gate)
            {
                if (!isCompleted)
                {
                    subscribers.Remove(subscriber);
                }
            }

            subscriber.DisposeSubscription();
        }

        private long GetCurrentEpoch()
        {
            lock (gate)
            {
                return epoch;
            }
        }

        private readonly record struct BufferedFrameItem(LiveProtocolFrame Frame, long Epoch);

        private sealed class BufferedFrameSubscriber
        {
            private readonly BufferedFrameStream owner;
            private readonly IObserver<LiveProtocolFrame> observer;
            private readonly Channel<BufferedFrameItem> channel;
            private readonly CancellationTokenSource disposeCts = new();
            private int completionMode;
            private int queuedCount;

            public BufferedFrameSubscriber(BufferedFrameStream owner, IObserver<LiveProtocolFrame> observer, int capacity)
            {
                this.owner = owner;
                this.observer = observer;
                channel = Channel.CreateBounded<BufferedFrameItem>(new BoundedChannelOptions(capacity)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                });
            }

            public void Start()
            {
                _ = Task.Factory.StartNew(
                    () => DrainAsync(),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            public bool Enqueue(BufferedFrameItem item)
            {
                var willDropOldest = Volatile.Read(ref queuedCount) >= owner.capacity;
                if (!channel.Writer.TryWrite(item))
                {
                    return false;
                }

                if (!willDropOldest)
                {
                    Interlocked.Increment(ref queuedCount);
                }

                return willDropOldest;
            }

            public void CompleteFromSource()
            {
                completionMode = 1;
                channel.Writer.TryComplete();
            }

            public void DisposeSubscription()
            {
                completionMode = 2;
                disposeCts.Cancel();
                channel.Writer.TryComplete();
            }

            private async Task DrainAsync()
            {
                try
                {
                    await foreach (var item in channel.Reader.ReadAllAsync(disposeCts.Token).ConfigureAwait(false))
                    {
                        Interlocked.Decrement(ref queuedCount);

                        if (item.Epoch != owner.GetCurrentEpoch())
                        {
                            continue;
                        }

                        observer.OnNext(item.Frame);
                    }

                    if (completionMode == 1)
                    {
                        observer.OnCompleted();
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private sealed class FrameSubscription(BufferedFrameStream owner, BufferedFrameSubscriber subscriber) : IDisposable
        {
            private int isDisposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref isDisposed, 1) != 0)
                {
                    return;
                }

                owner.Unsubscribe(subscriber);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();

            public void Dispose()
            {
            }
        }
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