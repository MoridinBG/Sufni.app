using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.App.LiveDaq.Services;
using Sufni.Profiling;
#endif
using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveDaqSharedStream : ILiveDaqSharedStream
{
    private const int FrameBufferCapacity = 1024;

    private static readonly ILogger logger = Log.ForContext<LiveDaqSharedStream>();

    private readonly ILiveDaqClientFactory liveDaqClientFactory;
    private readonly Func<LiveDaqSharedStream, Task> evictAsync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly BufferedFrameStream frames;
    private readonly BehaviorSubject<LiveDaqSharedStreamState> statesSubject;
    private readonly EventLoopScheduler clientEventScheduler = new();

    private LiveDaqSnapshot snapshot;
    private ILiveDaqClient? liveDaqClient;
    private IDisposable? liveDaqClientSubscription;
    private LiveDaqStreamConfiguration requestedConfiguration = LiveDaqStreamConfiguration.Default;
    private LiveDaqStreamConfiguration? activeConfiguration;
    private LiveDaqSharedStreamState currentState;
    private LifecycleOperation? pendingLifecycleOperation;
    private long activeClientGeneration;
    private long nextClientGeneration;
    private int observerCount;
    private int configurationLockCount;
    private bool desiredRunning;
    private bool isEvictionPending;
    private bool isDisposed;
    private bool isEvicted;
    private long evictionSequence;

    public LiveDaqSharedStream(
        LiveDaqSnapshot snapshot,
        ILiveDaqClientFactory liveDaqClientFactory,
        Func<LiveDaqSharedStream, Task> evictAsync)
    {
        this.snapshot = snapshot;
        this.liveDaqClientFactory = liveDaqClientFactory;
        this.evictAsync = evictAsync;
#if SUFNI_PROFILING_DIAGNOSTICS
        if (ProfilingLiveDaqReplay.Matches(snapshot.IdentityKey))
        {
            requestedConfiguration = ProfilingLiveDaqReplay.Configuration;
        }
#endif
        currentState = LiveDaqSharedStreamState.Empty with
        {
            ProtocolVersion = snapshot.ProtocolVersion,
        };
        statesSubject = new BehaviorSubject<LiveDaqSharedStreamState>(currentState);
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
        Task<LivePreviewStartResult?> waitTask;
        LifecycleOperation? operationToStart = null;

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            if (currentState.IsClosed || currentState.ConnectionState is LiveConnectionState.Connected)
            {
                return null;
            }

            desiredRunning = true;
            if (pendingLifecycleOperation is null)
            {
                if (!TryGetEndpoint(out _, out _))
                {
                    var failed = new LivePreviewStartResult.Failed("DAQ is offline.");
                    PublishDisconnectedStateLocked(failed.ErrorMessage);
                    return failed;
                }

                operationToStart = liveDaqClient is null
                    ? CreateLifecycleOperationLocked(requestedConfiguration)
                    : CreateLifecycleOperationForCurrentClientLocked();
                pendingLifecycleOperation = operationToStart;
                PublishConnectingStateLocked();
            }

            waitTask = pendingLifecycleOperation.Completion.Task;
        }
        finally
        {
            gate.Release();
        }

        if (operationToStart is not null)
        {
            _ = RunLifecycleOperationAsync(operationToStart);
        }

        return await WaitForLifecycleResultAsync(waitTask, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
#if SUFNI_PROFILING_DIAGNOSTICS
        using var profilingStage = ProfilingBench01.IsActive
            ? ProfilingRuntime.BeginStage(
                ProfilingBench01.Scenario,
                "LiveStream.Stop",
                IdentityKey)
            : null;
#endif
        Task<LivePreviewStartResult?>? waitTask = null;
        LifecycleOperation? operationToStart = null;

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            if (currentState.IsClosed)
            {
#if SUFNI_PROFILING_DIAGNOSTICS
                profilingStage?.SetResult(0, 0, "already_closed");
#endif
                return;
            }

            desiredRunning = false;
            if (pendingLifecycleOperation is null && liveDaqClient is null)
            {
                PublishDisconnectedStateLocked(null);
#if SUFNI_PROFILING_DIAGNOSTICS
                profilingStage?.SetResult(0, 0, "no_client");
#endif
                return;
            }

            if (pendingLifecycleOperation is null)
            {
                operationToStart = CreateLifecycleOperationForCurrentClientLocked();
                pendingLifecycleOperation = operationToStart;
            }

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnecting,
                LastError = null,
            });
            waitTask = pendingLifecycleOperation.Completion.Task;
        }
        finally
        {
            gate.Release();
        }

        if (operationToStart is not null)
        {
            _ = RunLifecycleOperationAsync(operationToStart);
        }

        if (waitTask is not null)
        {
            await WaitForLifecycleResultAsync(waitTask, cancellationToken);
        }
#if SUFNI_PROFILING_DIAGNOSTICS
        profilingStage?.SetResult(0, 0, "disconnected");
#endif
    }

    public async Task ApplyConfigurationAsync(LiveDaqStreamConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Task<LivePreviewStartResult?>? waitTask = null;
        LifecycleOperation? operationToStart = null;

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            if (currentState.IsClosed || currentState.IsConfigurationLocked || requestedConfiguration == configuration)
            {
                return;
            }

            requestedConfiguration = configuration;
            if (!desiredRunning && currentState.ConnectionState is LiveConnectionState.Disconnected)
            {
                return;
            }

            desiredRunning = true;
            if (pendingLifecycleOperation is null)
            {
                operationToStart = liveDaqClient is null
                    ? CreateLifecycleOperationLocked(configuration)
                    : CreateLifecycleOperationForCurrentClientLocked();
                pendingLifecycleOperation = operationToStart;
            }

            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnecting,
                LastError = null,
            });
            waitTask = pendingLifecycleOperation.Completion.Task;
        }
        finally
        {
            gate.Release();
        }

        if (operationToStart is not null)
        {
            _ = RunLifecycleOperationAsync(operationToStart);
        }

        if (waitTask is not null)
        {
            await WaitForLifecycleResultAsync(waitTask, cancellationToken);
        }
    }

    public async Task UpdateCatalogSnapshotAsync(LiveDaqSnapshot nextSnapshot, CancellationToken cancellationToken = default)
    {
        var protocolChanged = false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (isDisposed || currentState.IsClosed)
            {
                return;
            }

            protocolChanged = nextSnapshot.ProtocolVersion != snapshot.ProtocolVersion;
            snapshot = nextSnapshot;
            if (!protocolChanged)
            {
                PublishState(currentState);
            }
        }
        finally
        {
            gate.Release();
        }

        if (protocolChanged)
        {
            await CloseAsync("DAQ protocol changed. Reopen the live tab.", cancellationToken);
        }
    }

    public async Task CloseAsync(string errorMessage, CancellationToken cancellationToken = default)
    {
        DetachedClient? detachedClient;

        await gate.WaitAsync(CancellationToken.None);
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

            desiredRunning = false;
            CompletePendingLifecycleLocked(null);
            detachedClient = DetachCurrentClientLocked();
            PublishState(currentState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = errorMessage,
                SessionHeader = null,
                SelectedStreamMask = LiveStreamMask.None,
                IsClosed = true,
            });
            isEvictionPending = true;
        }
        finally
        {
            gate.Release();
        }

        await DisposeDetachedClientAsync(detachedClient);
        await EvictAsync();
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        DetachedClient? detachedClient;

        try
        {
            await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
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

            isDisposed = true;
            desiredRunning = false;
            CompletePendingLifecycleLocked(null);
            detachedClient = DetachCurrentClientLocked();
        }
        finally
        {
            gate.Release();
        }

        await DisposeDetachedClientAsync(detachedClient).ConfigureAwait(false);

        statesSubject.OnCompleted();
        frames.Complete();
        statesSubject.Dispose();
        clientEventScheduler.Dispose();
        gate.Dispose();
    }

    private async Task RunLifecycleOperationAsync(LifecycleOperation operation)
    {
        while (true)
        {
            string host;
            int port;
            bool shouldRun;
            bool replaceAfterCleanup = false;
            LiveDaqStreamConfiguration configuration;

            try
            {
                await gate.WaitAsync(CancellationToken.None);
            }
            catch (ObjectDisposedException)
            {
                operation.Completion.TrySetResult(null);
                return;
            }

            try
            {
                if (!IsCurrentOperationLocked(operation))
                {
                    operation.Completion.TrySetResult(null);
                    return;
                }

                shouldRun = desiredRunning;
                configuration = requestedConfiguration;
                if (!shouldRun)
                {
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnecting,
                        LastError = null,
                    });
                    operation.DetachedClient = DetachCurrentClientLocked(operation.Client, operation.Generation);
                }
                else if (activeConfiguration is not null && activeConfiguration != configuration)
                {
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnecting,
                        LastError = null,
                    });
                    operation.DetachedClient = DetachCurrentClientLocked(operation.Client, operation.Generation);
                    replaceAfterCleanup = true;
                }
                else if (!TryGetEndpoint(out host, out port))
                {
                    var failed = new LivePreviewStartResult.Failed("DAQ is offline.");
                    operation.DetachedClient = DetachCurrentClientLocked(operation.Client, operation.Generation);
                    PublishDisconnectedStateLocked(failed.ErrorMessage);
                    pendingLifecycleOperation = null;
                    operation.Completion.TrySetResult(failed);
                    shouldRun = false;
                }
                else
                {
                    operation.Configuration = configuration;
                    PublishConnectingStateLocked();
                    goto RunTransport;
                }
            }
            finally
            {
                gate.Release();
            }

            await StopDisconnectAndDisposeAsync(operation.DetachedClient, stopPreview: true);
            operation.DetachedClient = null;

            if (!shouldRun)
            {
                if (await CompleteStoppedOperationAsync(operation))
                {
                    continue;
                }

                return;
            }

            if (replaceAfterCleanup)
            {
                try
                {
                    await gate.WaitAsync(CancellationToken.None);
                }
                catch (ObjectDisposedException)
                {
                    operation.Completion.TrySetResult(null);
                    return;
                }

                try
                {
                    if (!ReferenceEquals(pendingLifecycleOperation, operation) || isDisposed || currentState.IsClosed)
                    {
                        operation.Completion.TrySetResult(null);
                        return;
                    }

                    if (!desiredRunning)
                    {
                        PublishDisconnectedStateLocked(null);
                        pendingLifecycleOperation = null;
                        operation.Completion.TrySetResult(null);
                        return;
                    }

                    var replacement = CreateClientBindingLocked();
                    operation.Client = replacement.Client;
                    operation.Generation = replacement.Generation;
                    operation.Configuration = requestedConfiguration;
                }
                finally
                {
                    gate.Release();
                }
            }

            continue;

        RunTransport:
            LivePreviewStartResult result;
            try
            {
                if (!operation.Client.IsConnected)
                {
                    await operation.Client.ConnectAsync(host, port, CancellationToken.None).ConfigureAwait(false);
                }

                result = await operation.Client.StartPreviewAsync(
                        operation.Configuration.ToStartRequest(),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await CompleteCanceledOperationAsync(operation);
                return;
            }
            catch (Exception ex)
            {
                result = new LivePreviewStartResult.Failed(ex.Message);
            }

            bool shouldConverge;
            DetachedClient? detachedForConvergence = null;
            var completeAfterCleanup = false;

            try
            {
                await gate.WaitAsync(CancellationToken.None);
            }
            catch (ObjectDisposedException)
            {
                operation.Completion.TrySetResult(null);
                return;
            }

            try
            {
                if (!IsCurrentOperationLocked(operation))
                {
                    operation.Completion.TrySetResult(null);
                    return;
                }

                shouldConverge = !desiredRunning || requestedConfiguration != operation.Configuration;
                if (shouldConverge)
                {
                    detachedForConvergence = DetachCurrentClientLocked(operation.Client, operation.Generation);
                    PublishState(currentState with
                    {
                        ConnectionState = LiveConnectionState.Disconnecting,
                        LastError = null,
                    });
                }
                else
                {
                    switch (result)
                    {
                        case LivePreviewStartResult.Started started:
                            PublishSuccessfulStartLocked(started, operation.Configuration);
                            pendingLifecycleOperation = null;
                            operation.Completion.TrySetResult(started);
                            return;

                        case LivePreviewStartResult.Rejected rejected:
                            detachedForConvergence = DetachCurrentClientLocked(operation.Client, operation.Generation);
                            PublishDisconnectedStateLocked(rejected.UserMessage);
                            completeAfterCleanup = true;
                            break;

                        case LivePreviewStartResult.Failed failed:
                            PublishDisconnectedStateLocked(failed.ErrorMessage);
                            pendingLifecycleOperation = null;
                            operation.Completion.TrySetResult(failed);
                            return;
                    }
                }
            }
            finally
            {
                gate.Release();
            }

            await StopDisconnectAndDisposeAsync(
                detachedForConvergence,
                stopPreview: result is LivePreviewStartResult.Started);

            if (completeAfterCleanup)
            {
                await CompleteOperationAfterCleanupAsync(operation, result);
                return;
            }

            try
            {
                await gate.WaitAsync(CancellationToken.None);
            }
            catch (ObjectDisposedException)
            {
                operation.Completion.TrySetResult(null);
                return;
            }

            try
            {
                if (!ReferenceEquals(pendingLifecycleOperation, operation) || isDisposed || currentState.IsClosed)
                {
                    operation.Completion.TrySetResult(null);
                    return;
                }

                if (!desiredRunning)
                {
                    PublishDisconnectedStateLocked(null);
                    pendingLifecycleOperation = null;
                    operation.Completion.TrySetResult(null);
                    return;
                }

                var replacement = CreateClientBindingLocked();
                operation.Client = replacement.Client;
                operation.Generation = replacement.Generation;
                operation.Configuration = requestedConfiguration;
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task<bool> CompleteStoppedOperationAsync(LifecycleOperation operation)
    {
        try
        {
            await gate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            operation.Completion.TrySetResult(null);
            return false;
        }

        try
        {
            if (!ReferenceEquals(pendingLifecycleOperation, operation) || isDisposed || currentState.IsClosed)
            {
                operation.Completion.TrySetResult(null);
                return false;
            }

            if (desiredRunning)
            {
                var replacement = CreateClientBindingLocked();
                operation.Client = replacement.Client;
                operation.Generation = replacement.Generation;
                operation.Configuration = requestedConfiguration;
                return true;
            }

            PublishDisconnectedStateLocked(null);
            pendingLifecycleOperation = null;
            operation.Completion.TrySetResult(null);
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task CompleteCanceledOperationAsync(LifecycleOperation operation)
    {
        DetachedClient? detachedClient = null;
        try
        {
            await gate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            operation.Completion.TrySetResult(null);
            return;
        }

        try
        {
            if (IsCurrentOperationLocked(operation))
            {
                detachedClient = DetachCurrentClientLocked(operation.Client, operation.Generation);
                PublishDisconnectedStateLocked(null);
                pendingLifecycleOperation = null;
            }

            operation.Completion.TrySetResult(null);
        }
        finally
        {
            gate.Release();
        }

        await StopDisconnectAndDisposeAsync(detachedClient, stopPreview: false);
    }

    private async Task CompleteOperationAfterCleanupAsync(
        LifecycleOperation operation,
        LivePreviewStartResult result)
    {
        try
        {
            await gate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            operation.Completion.TrySetResult(null);
            return;
        }

        try
        {
            if (!ReferenceEquals(pendingLifecycleOperation, operation))
            {
                operation.Completion.TrySetResult(null);
                return;
            }

            pendingLifecycleOperation = null;
            operation.Completion.TrySetResult(result);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<LivePreviewStartResult?> WaitForLifecycleResultAsync(
        Task<LivePreviewStartResult?> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private LifecycleOperation CreateLifecycleOperationLocked(LiveDaqStreamConfiguration configuration)
    {
        var binding = CreateClientBindingLocked();
        return new LifecycleOperation(binding.Client, binding.Generation, configuration);
    }

    private LifecycleOperation CreateLifecycleOperationForCurrentClientLocked()
    {
        if (liveDaqClient is null)
        {
            return CreateLifecycleOperationLocked(requestedConfiguration);
        }

        return new LifecycleOperation(liveDaqClient, activeClientGeneration, requestedConfiguration);
    }

    private ClientBinding CreateClientBindingLocked()
    {
        var client = liveDaqClientFactory.Create(snapshot);
        var generation = ++nextClientGeneration;
        liveDaqClient = client;
        activeClientGeneration = generation;
        liveDaqClientSubscription = client.Events
            .ObserveOn(clientEventScheduler)
            .Subscribe(clientEvent => _ = HandleClientEventAsync(client, generation, clientEvent));
        return new ClientBinding(client, generation);
    }

    private bool IsCurrentOperationLocked(LifecycleOperation operation) =>
        ReferenceEquals(pendingLifecycleOperation, operation)
        && ReferenceEquals(liveDaqClient, operation.Client)
        && activeClientGeneration == operation.Generation
        && !isDisposed
        && !currentState.IsClosed;

    private DetachedClient? DetachCurrentClientLocked(
        ILiveDaqClient? expectedClient = null,
        long? expectedGeneration = null)
    {
        if (liveDaqClient is null
            || (expectedClient is not null && !ReferenceEquals(liveDaqClient, expectedClient))
            || (expectedGeneration is not null && activeClientGeneration != expectedGeneration))
        {
            return null;
        }

        var detached = new DetachedClient(liveDaqClient, liveDaqClientSubscription);
        liveDaqClient = null;
        liveDaqClientSubscription = null;
        activeClientGeneration = 0;
        activeConfiguration = null;
        detached.Subscription?.Dispose();
        return detached;
    }

    private void CompletePendingLifecycleLocked(LivePreviewStartResult? result)
    {
        var pending = pendingLifecycleOperation;
        pendingLifecycleOperation = null;
        pending?.Completion.TrySetResult(result);
    }

    private static async Task StopDisconnectAndDisposeAsync(DetachedClient? detachedClient, bool stopPreview)
    {
        if (detachedClient is null)
        {
            return;
        }

        if (stopPreview)
        {
            try
            {
                await detachedClient.Client.StopPreviewAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Stopping detached shared live DAQ client failed");
            }
        }

        try
        {
            await detachedClient.Client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Disconnecting detached shared live DAQ client failed");
        }

        await DisposeDetachedClientAsync(detachedClient).ConfigureAwait(false);
    }

    private static async Task DisposeDetachedClientAsync(DetachedClient? detachedClient)
    {
        if (detachedClient is null)
        {
            return;
        }

        try
        {
            await detachedClient.Client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Disposing detached shared live DAQ client failed");
        }
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

    private async Task HandleClientEventAsync(
        ILiveDaqClient client,
        long generation,
        LiveDaqClientEvent clientEvent)
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
            if (isDisposed
                || !ReferenceEquals(liveDaqClient, client)
                || activeClientGeneration != generation)
            {
                return;
            }

            switch (clientEvent)
            {
                case LiveDaqClientEvent.FrameReceived frameReceived:
                    if (frameReceived.Frame is LiveErrorFrame errorFrame)
                    {
                        PublishDisconnectedStateLocked(errorFrame.Payload.ErrorCode.UserMessage);
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

    private void PublishSuccessfulStartLocked(
        LivePreviewStartResult.Started started,
        LiveDaqStreamConfiguration configuration)
    {
        logger.Information(
            "Shared live DAQ stream connected for {IdentityKey} at {Endpoint} with session {SessionId}",
            IdentityKey,
            snapshot.Endpoint,
            started.Header.SessionId);
        activeConfiguration = configuration;
        PublishState(currentState with
        {
            ConnectionState = LiveConnectionState.Connected,
            LastError = null,
            SessionHeader = started.Header,
            SelectedStreamMask = started.Header.AcceptedStreamMask,
            ClientDropCounters = LiveDaqClientDropCounters.Empty,
        });
    }

    private void PublishConnectingStateLocked()
    {
        PublishState(currentState with
        {
            ConnectionState = LiveConnectionState.Connecting,
            LastError = null,
            SessionHeader = null,
            SelectedStreamMask = LiveStreamMask.None,
        });
    }

    private void PublishDisconnectedStateLocked(string? errorMessage)
    {
        activeConfiguration = null;
        PublishState(currentState with
        {
            ConnectionState = LiveConnectionState.Disconnected,
            LastError = errorMessage,
            SessionHeader = null,
            SelectedStreamMask = LiveStreamMask.None,
        });
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
            ProtocolVersion = snapshot.ProtocolVersion,
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
        DetachedClient? detachedClient = null;
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

            desiredRunning = false;
            CompletePendingLifecycleLocked(null);
            detachedClient = DetachCurrentClientLocked();
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

        await DisposeDetachedClientAsync(detachedClient);

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

    private sealed class LifecycleOperation(
        ILiveDaqClient client,
        long generation,
        LiveDaqStreamConfiguration configuration)
    {
        public ILiveDaqClient Client { get; set; } = client;
        public long Generation { get; set; } = generation;
        public LiveDaqStreamConfiguration Configuration { get; set; } = configuration;
        public TaskCompletionSource<LivePreviewStartResult?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DetachedClient? DetachedClient { get; set; }
    }

    private readonly record struct ClientBinding(ILiveDaqClient Client, long Generation);

    private sealed record DetachedClient(ILiveDaqClient Client, IDisposable? Subscription);

    private sealed class BufferedFrameStream : IObservable<LiveProtocolFrame>
    {
        private readonly System.Threading.Lock gate = new();
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
