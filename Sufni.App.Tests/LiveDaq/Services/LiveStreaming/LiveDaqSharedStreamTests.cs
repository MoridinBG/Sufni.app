using System.Reactive.Subjects;
using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public partial class LiveDaqSharedStreamTests
{
    private const int DeadlockProneTestTimeoutMs = 5000;
    private const int SlowSubscriberTestTimeoutMs = 15000;

    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> catalogEntries = new([]);
    private readonly ILiveDaqCatalogService catalogService = Substitute.For<ILiveDaqCatalogService>();
    private readonly IDisposable browseLease = Substitute.For<IDisposable>();
    private readonly FakeLiveDaqClientFactory clientFactory = new();

    public LiveDaqSharedStreamTests()
    {
        catalogService.Observe().Returns(catalogEntries);
        catalogService.Get(Arg.Any<string>()).Returns(call =>
            catalogEntries.Value.FirstOrDefault(entry => entry.IdentityKey == call.Arg<string>()));
        catalogService.AcquireBrowse().Returns(browseLease);
    }

    [Fact]
    public async Task ConnectLiveAsync_Offline_ReturnsUnavailableWithoutCreatingClient()
    {
        await using var registry = CreateRegistry();
        var stream = registry.GetOrCreate(CreateSnapshot("offline", "192.168.0.50", 1557) with { IsOnline = false });
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;

        var result = await stream.ConnectLiveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LiveStartUnavailableReason.Offline, Assert.IsType<LivePreviewStartResult.Unavailable>(result).Reason);
        Assert.Equal(result, Assert.IsType<LiveConnectionState.Disconnected>(stream.CurrentState.ConnectionState).Outcome);
        Assert.Empty(clientFactory.CreatedClients);
    }

    [Fact]
    public async Task ConnectLiveAsync_UsesCurrentCatalogEndpointInsteadOfTabSnapshot()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        var endpoint = new ServiceEndpoint([System.Net.IPAddress.Parse("192.168.0.51"), System.Net.IPAddress.IPv6Loopback], 1666);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot) with { NetworkEndpoint = endpoint }]);
        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;

        Assert.IsType<LivePreviewStartResult.Started>(await stream.ConnectLiveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(endpoint, Assert.Single(clientFactory.CreatedClients).ConnectedEndpoint);
    }

    [Fact]
    public async Task CatalogProtocolChange_ClosesAndEvictsActiveStream()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557, LiveProtocolVersion.V2);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = stream.States.Subscribe(state =>
        {
            if (state.IsClosed)
            {
                closed.TrySetResult();
            }
        });

        catalogEntries.OnNext([CreateCatalogEntry(snapshot with { ProtocolVersion = LiveProtocolVersion.V3 })]);

        await closed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.True(stream.CurrentState.IsClosed);
        Assert.Equal(LiveStreamClosedReason.ProtocolChanged,
            Assert.IsType<LiveStreamLeaseResult.Closed>(stream.AcquireLease()).State.Reason);

        var replacement = registry.GetOrCreate(snapshot with { ProtocolVersion = LiveProtocolVersion.V3 });
        await using var replacementLease = Assert.IsType<LiveStreamLeaseResult.Acquired>(replacement.AcquireLease()).Lease;
        Assert.IsType<LivePreviewStartResult.Started>(await replacement.ConnectLiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_CompletesObservables_DisposesClient_AndFutureLeaseAcquisitionReturnsClosed()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        var statesCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var framesCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var statesSubscription = stream.States.Subscribe(
            _ => { },
            ex => statesCompleted.TrySetException(ex),
            () => statesCompleted.TrySetResult());
        using var framesSubscription = stream.Frames.Subscribe(
            _ => { },
            ex => framesCompleted.TrySetException(ex),
            () => framesCompleted.TrySetResult());
        var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);

        await stream.DisposeAsync();
        await statesCompleted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await framesCompleted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stream.DisposeAsync();

        Assert.Equal(LiveStreamClosedReason.Disposed, Assert.IsType<LiveStreamLeaseResult.Closed>(stream.AcquireLease()).State.Reason);

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task CatalogRemoval_ClosesAndEvictsActiveStream()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = stream.States.Subscribe(state =>
        {
            if (state.IsClosed)
            {
                closed.TrySetResult();
            }
        });

        catalogEntries.OnNext([]);
        await closed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.True(stream.CurrentState.IsClosed);
        Assert.Equal(LiveStreamClosedReason.Offline,
            Assert.IsType<LiveStreamLeaseResult.Closed>(stream.AcquireConfigurationLock()).State.Reason);
        var replacement = registry.GetOrCreate(snapshot);
        var unavailable = Assert.IsType<LivePreviewStartResult.Unavailable>(await replacement.ConnectLiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(LiveStartUnavailableReason.Offline, unavailable.Reason);
    }

    [Fact]
    public async Task ReconfigureFailure_LeavesStreamRecoverableInsteadOfClosed()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);

        clientFactory.ConfigureBeforeReturn = client =>
        {
            client.FailNextStartPreview = true;
            clientFactory.ConfigureBeforeReturn = null;
        };

        await stream.ApplyConfigurationAsync(LiveDaqStreamConfiguration.FromRequestedRates(100, 0, 5), cancellationToken: TestContext.Current.CancellationToken);

        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveStartFailureReason.Request, Assert.IsType<LivePreviewStartResult.Failed>(
            Assert.IsType<LiveConnectionState.Disconnected>(stream.CurrentState.ConnectionState).Outcome).Reason);

        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(new LiveConnectionState.Connected(), stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task ConnectLiveAsync_SharesPendingStart_AndCallerCancellationDoesNotCancelTransport()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingStartCompletion = pendingStart;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        using var canceledWaiter = new CancellationTokenSource();

        var firstWait = stream.ConnectLiveAsync(canceledWaiter.Token);
        var client = clientFactory.CreatedClients.Single();
        await client.StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var secondWait = stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);

        canceledWaiter.Cancel();
        Assert.Null(await firstWait);
        Assert.False(secondWait.IsCompleted);
        Assert.Equal(1, client.StartCalls);

        var acceptedMask = LiveStreamMask.Gps;
        pendingStart.SetResult(CreateStartedResult(901, acceptedMask));

        var started = Assert.IsType<LivePreviewStartResult.Started>(await secondWait);
        Assert.Equal(acceptedMask, started.Header.AcceptedStreamMask);
        Assert.Equal(acceptedMask, stream.CurrentState.SelectedStreamMask);
        Assert.Equal(1, client.StartCalls);
    }

    [Fact]
    public async Task ApplyConfigurationAsync_DuringPendingStart_ConvergesToLatestConfiguration()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client =>
        {
            if (clientFactory.CreatedClients.Count == 0)
            {
                client.PendingStartCompletion = pendingStart;
            }
            else
            {
                client.AcceptedStreamMask = LiveStreamMask.Gps;
            }
        };

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        var initialStart = stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var firstClient = clientFactory.CreatedClients.Single();
        await firstClient.StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var latestConfiguration = new LiveDaqStreamConfiguration(
            RequestedStreamMask: LiveStreamMask.Gps,
            RequestedSensorMask: LiveSensorInstanceMask.Gps,
            TravelRateMhz: 0,
            ImuRateMhz: 0,
            GpsRateMhz: 5_000);
        var reconfigure = stream.ApplyConfigurationAsync(latestConfiguration, cancellationToken: TestContext.Current.CancellationToken);

        pendingStart.SetResult(CreateStartedResult(902, LiveStreamMask.Travel));
        await initialStart.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await reconfigure.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        var replacement = clientFactory.CreatedClients[1];
        var request = Assert.Single(replacement.StartRequests);
        Assert.Equal(latestConfiguration.RequestedStreamMask, request.RequestedStreamMask);
        Assert.Equal(latestConfiguration.GpsRateMhz, request.GpsRateMhz);
        Assert.Equal(LiveStreamMask.Gps, stream.CurrentState.SelectedStreamMask);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task LifecycleTransportWaits_DoNotHoldSharedGate()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var connectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingConnectCompletion = connectRelease;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        var start = stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var client = clientFactory.CreatedClients.Single();
        await client.ConnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var nextConfiguration = LiveDaqStreamConfiguration.FromRequestedRates(100, 20, 5);
        var reconfigure = stream.ApplyConfigurationAsync(nextConfiguration, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(nextConfiguration, stream.RequestedConfiguration);
        await using var concurrentLease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;

        connectRelease.SetResult();
        await start.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await reconfigure.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAsync_DuringPendingStart_InvalidatesLateCompletion_AndPermitsRestart()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client =>
        {
            if (clientFactory.CreatedClients.Count == 0)
            {
                client.PendingStartCompletion = pendingStart;
            }
        };

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        var start = stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var oldClient = clientFactory.CreatedClients.Single();
        await oldClient.StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var stop = stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
        pendingStart.SetResult(CreateStartedResult(903, LiveStreamMask.Gps));
        await start.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new LiveConnectionState.Disconnected(), stream.CurrentState.ConnectionState);
        Assert.Equal(LiveStreamMask.None, stream.CurrentState.SelectedStreamMask);

        var restarted = await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(restarted);
        Assert.Equal(new LiveConnectionState.Connected(), stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task DisposeAsync_DuringPendingStart_IgnoresLateCompletion()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingStartCompletion = pendingStart;

        var stream = registry.GetOrCreate(snapshot);
        var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        var start = stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        await clientFactory.CreatedClients.Single().StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await stream.DisposeAsync();
        pendingStart.SetResult(CreateStartedResult(905, LiveStreamMask.Gps));
        Assert.Equal(LiveStreamClosedReason.Disposed, Assert.IsType<LivePreviewStartResult.Closed>(await start).State.Reason);
        Assert.Equal(LiveStreamClosedReason.Disposed, Assert.IsType<LiveStreamLeaseResult.Closed>(stream.AcquireLease()).State.Reason);

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task OldGenerationEvents_CannotMutateReplacementStateOrPublishFrames()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        clientFactory.ConfigureBeforeReturn = client =>
            client.AcceptedStreamMask = clientFactory.CreatedClients.Count == 0
                ? LiveStreamMask.Travel
                : LiveStreamMask.Gps;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var oldClient = clientFactory.CreatedClients.Single();
        var observedFrames = new List<LiveProtocolFrame>();
        using var subscription = stream.Frames.Subscribe(observedFrames.Add);

        var configuration = new LiveDaqStreamConfiguration(
            RequestedStreamMask: LiveStreamMask.Gps,
            RequestedSensorMask: LiveSensorInstanceMask.Gps,
            TravelRateMhz: 0,
            ImuRateMhz: 0,
            GpsRateMhz: 5_000);
        await stream.ApplyConfigurationAsync(configuration, cancellationToken: TestContext.Current.CancellationToken);

        oldClient.PublishFrame(CreateTravelBatchFrame(77));
        oldClient.PublishFault("stale fault");
        oldClient.PublishDisconnectedEvent("stale disconnect");
        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(new LiveConnectionState.Connected(), stream.CurrentState.ConnectionState);
        Assert.Equal(LiveStreamMask.Gps, stream.CurrentState.SelectedStreamMask);
        Assert.DoesNotContain(observedFrames, frame => frame is LiveTravelBatchFrame travel && travel.Batch.FirstMonotonicUs == 77);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task StopAsync_DoesNotHoldGate_WhileStopOrDisconnectIsPending()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var client = clientFactory.CreatedClients.Single();
        var stopRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingStopCompletion = stopRelease;
        client.PendingDisconnectCompletion = disconnectRelease;

        var stop = stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.StopEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await using var leaseWhileStopping = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;

        stopRelease.SetResult();
        await client.DisconnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await using var leaseWhileDisconnecting = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;

        disconnectRelease.SetResult();
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new LiveConnectionState.Disconnected(), stream.CurrentState.ConnectionState);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task ConnectLiveAsync_DuringPendingStop_ReconvergesToRunning()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var oldClient = clientFactory.CreatedClients.Single();
        var stopRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldClient.PendingStopCompletion = stopRelease;
        oldClient.PendingDisconnectCompletion = disconnectRelease;

        var stop = stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
        await oldClient.StopEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var restart = stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(restart.IsCompleted);

        stopRelease.SetResult();
        await oldClient.DisconnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        disconnectRelease.SetResult();

        var restarted = await restart.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Started>(restarted);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.Single(clientFactory.CreatedClients[1].StartRequests);
        Assert.Equal(new LiveConnectionState.Connected(), stream.CurrentState.ConnectionState);
    }

    [Theory]
    [InlineData(CanceledOperation.Connect)]
    [InlineData(CanceledOperation.Stop)]
    [InlineData(CanceledOperation.ApplyConfiguration)]
    public async Task Operations_WhenClientThrowsCanceled_DoNotPublishError(CanceledOperation operation)
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        if (operation is CanceledOperation.Connect)
        {
            clientFactory.ConfigureBeforeReturn = c => c.ThrowCanceledOnConnect = true;
        }

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        if (operation is not CanceledOperation.Connect)
        {
            await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(new LiveConnectionState.Connected(), stream.CurrentState.ConnectionState);
            clientFactory.CreatedClients.Single().ThrowCanceledOnDisconnect = true;
        }

        switch (operation)
        {
            case CanceledOperation.Connect:
                Assert.Null(await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken));
                break;
            case CanceledOperation.Stop:
                await stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
                break;
            case CanceledOperation.ApplyConfiguration:
                await stream.ApplyConfigurationAsync(LiveDaqStreamConfiguration.FromRequestedRates(100, 0, 5), cancellationToken: TestContext.Current.CancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }

        Assert.Null(stream.CurrentState.LastError);
        Assert.False(stream.CurrentState.IsClosed);
    }

    [Fact(Timeout = SlowSubscriberTestTimeoutMs)]
    public async Task Frames_SlowSubscriber_DoesNotBlockPublishing_AndDropsOldestBufferedFrames()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(cancellationToken: TestContext.Current.CancellationToken);

        var client = clientFactory.CreatedClients.Single();
        var firstFrameEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedOffsets = new List<ulong>();

        using var subscription = stream.Frames.Subscribe(frame =>
        {
            if (frame is not LiveTravelBatchFrame travelFrame)
            {
                return;
            }

            if (!firstFrameEntered.Task.IsCompleted)
            {
                firstFrameEntered.TrySetResult();
                releaseFirstFrame.Task.GetAwaiter().GetResult();
            }

            lock (receivedOffsets)
            {
                receivedOffsets.Add(travelFrame.Batch.FirstMonotonicUs);
            }
        });

        const int publishedFrameCount = 4096;
        var publishTask = Task.Run(() =>
        {
            for (var index = 1; index <= publishedFrameCount; index++)
            {
                client.PublishFrame(CreateTravelBatchFrame((ulong)index));
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        await firstFrameEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var publishCompletedWhileSubscriberBlocked =
            await Task.WhenAny(
                publishTask,
                Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)) == publishTask;
        if (!publishCompletedWhileSubscriberBlocked)
        {
            releaseFirstFrame.TrySetResult();
            await publishTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        }

        Assert.True(
            publishCompletedWhileSubscriberBlocked,
            "Publishing blocked behind a slow frame subscriber.");

        try
        {
            await AssertEventuallyAsync(
                () => stream.CurrentState.ClientDropCounters.SubscriberFramesDropped > 0,
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            releaseFirstFrame.TrySetResult();
        }

        await publishTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await AssertEventuallyAsync(() =>
        {
            lock (receivedOffsets)
            {
                return receivedOffsets.Count > 0 && receivedOffsets.Contains((ulong)publishedFrameCount);
            }
        });

        lock (receivedOffsets)
        {
            Assert.True(receivedOffsets.Count < publishedFrameCount);
            Assert.Contains((ulong)publishedFrameCount, receivedOffsets);
            // Depending on when the drain task starts, the first delivered
            // frame can be either an early frame or the retained suffix.
            // In both valid interleavings, at least one published offset is
            // missing before the latest retained frame.
            Assert.True(
                receivedOffsets[0] > 1 ||
                receivedOffsets.Zip(receivedOffsets.Skip(1), (previous, current) => current > previous + 1).Any(hasGap => hasGap));
        }

        Assert.True(stream.CurrentState.ClientDropCounters.SubscriberFramesDropped > 0);
    }

    private LiveDaqSharedStreamRegistry CreateRegistry() =>
        new(clientFactory, catalogService);

    private static LiveDaqSnapshot CreateSnapshot(
        string identityKey,
        string host,
        int port,
        LiveProtocolVersion protocolVersion = LiveProtocolVersion.V2) =>
        new(
            IdentityKey: identityKey,
            DisplayName: identityKey,
            BoardId: identityKey,
            Host: host,
            Port: port,
            IsOnline: true,
            SetupName: "setup",
            BikeName: "bike",
            ProtocolVersion: protocolVersion);

    private static LiveDaqCatalogEntry CreateCatalogEntry(LiveDaqSnapshot snapshot) =>
        new(
            snapshot.IdentityKey,
            snapshot.DisplayName,
            snapshot.BoardId,
            new ServiceEndpoint(snapshot.Host!, snapshot.Port!.Value),
            snapshot.ProtocolVersion);

    private static LivePreviewStartResult.Started CreateStartedResult(uint sessionId, LiveStreamMask acceptedStreamMask) =>
        new(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: sessionId) with
        {
            AcceptedStreamMask = acceptedStreamMask,
        });

    private static LiveTravelBatchFrame CreateTravelBatchFrame(ulong firstMonotonicUs) =>
        new(
            new LiveFrameMetadata(0),
            new LiveBatchHeader(901, 0, 0, firstMonotonicUs, 1),
            [new LiveTravelRecord((ushort)firstMonotonicUs, (ushort)firstMonotonicUs)]);

    private static async Task AssertEventuallyAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            await Task.Delay(1, timeoutCts.Token);
        }
    }

    private sealed class FakeLiveDaqClientFactory : ILiveDaqClientFactory
    {
        public List<FakeLiveDaqClient> CreatedClients { get; } = [];
        public List<LiveDaqSnapshot> CreatedForSnapshots { get; } = [];

        public Action<FakeLiveDaqClient>? ConfigureBeforeReturn { get; set; }

        public ILiveDaqClient Create(LiveDaqSnapshot snapshot)
        {
            var client = new FakeLiveDaqClient();
            ConfigureBeforeReturn?.Invoke(client);
            CreatedClients.Add(client);
            CreatedForSnapshots.Add(snapshot);
            return client;
        }
    }

    private sealed class FakeLiveDaqClient : ILiveDaqClient
    {
        public const uint DisconnectFlushMarkerSessionId = 4_242;

        private readonly Subject<LiveDaqClientEvent> events = new();
        private readonly TaskCompletionSource disposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int pendingDisconnectEvents;

        public bool FailNextStartPreview { get; set; }

        public TaskCompletionSource<LivePreviewStartResult>? PendingStartCompletion { get; set; }

        public TaskCompletionSource? PendingConnectCompletion { get; set; }

        public TaskCompletionSource? PendingStopCompletion { get; set; }

        public TaskCompletionSource? PendingDisconnectCompletion { get; set; }

        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ConnectEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisconnectEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<LiveStartRequest> StartRequests { get; } = [];

        public int StartCalls { get; private set; }

        public bool RejectNextStartPreview { get; set; }

        public LiveStartErrorCode RejectErrorCode { get; set; } = LiveStartErrorCode.Busy;

        public LiveSensorInstanceMask? AcceptedSensorMask { get; set; }

        public LiveStreamMask? AcceptedStreamMask { get; set; }

        public bool DelayDisconnectEvents { get; set; }

        public bool BlockDisposeAsync { get; set; }

        public bool ThrowCanceledOnConnect { get; set; }

        public bool ThrowCanceledOnStartPreview { get; set; }

        public bool ThrowCanceledOnDisconnect { get; set; }

        public bool IsConnected { get; private set; }

        public int ConnectCalls { get; private set; }

        public ServiceEndpoint? ConnectedEndpoint { get; private set; }

        public int DisconnectCalls { get; private set; }

        public List<string> StopLifecycleCalls { get; } = [];


        public IObservable<LiveDaqClientEvent> Events => events;

        public Task DisposeStarted => disposeStarted.Task;

        public async Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            ConnectedEndpoint = endpoint;
            ConnectEntered.TrySetResult();
            if (ThrowCanceledOnConnect)
            {
                ThrowCanceledOnConnect = false;
                throw new OperationCanceledException();
            }

            if (PendingConnectCompletion is not null)
            {
                await PendingConnectCompletion.Task;
            }

            IsConnected = true;
        }

        public async Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            StartRequests.Add(request);
            StartEntered.TrySetResult();
            if (ThrowCanceledOnStartPreview)
            {
                ThrowCanceledOnStartPreview = false;
                throw new OperationCanceledException();
            }

            if (PendingStartCompletion is not null)
            {
                return await PendingStartCompletion.Task;
            }

            if (FailNextStartPreview)
            {
                FailNextStartPreview = false;
                return new LivePreviewStartResult.Failed(LiveStartFailureReason.Request, "preview start failed");
            }

            if (RejectNextStartPreview)
            {
                RejectNextStartPreview = false;
                return new LivePreviewStartResult.Rejected(RejectErrorCode, RejectErrorCode.UserMessage);
            }

            var acceptedSensorMask = AcceptedSensorMask ?? request.RequestedSensorMask;
            var acceptedStreamMask = AcceptedStreamMask ?? acceptedSensorMask.StreamMask;
            var header = LiveProtocolTestFrames.CreateSessionHeaderModel(
                sessionId: (uint)(900 + ConnectCalls),
                requestedSensorMask: request.RequestedSensorMask,
                acceptedSensorMask: acceptedSensorMask) with
            {
                AcceptedStreamMask = acceptedStreamMask,
            };
            events.OnNext(new LiveDaqClientEvent.FrameReceived(
                new LiveStartAckFrame(
                    new LiveFrameMetadata(0),
                    new LiveStartAck(LiveStartErrorCode.Ok, header.SessionId, acceptedStreamMask))));
            events.OnNext(new LiveDaqClientEvent.FrameReceived(
                new LiveSessionHeaderFrame(
                    new LiveFrameMetadata(0),
                    header)));

            return new LivePreviewStartResult.Started(header);
        }

        public async Task StopPreviewAsync(CancellationToken cancellationToken = default)
        {
            StopLifecycleCalls.Add("stop");
            StopEntered.TrySetResult();
            if (PendingStopCompletion is not null)
            {
                await PendingStopCompletion.Task;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            StopLifecycleCalls.Add("disconnect");
            DisconnectEntered.TrySetResult();
            DisconnectCalls++;
            if (ThrowCanceledOnDisconnect)
            {
                ThrowCanceledOnDisconnect = false;
                throw new OperationCanceledException();
            }

            if (PendingDisconnectCompletion is not null)
            {
                await PendingDisconnectCompletion.Task;
            }

            IsConnected = false;
            PublishDisconnected();
        }

        public void PublishFrame(LiveProtocolFrame frame)
        {
            events.OnNext(new LiveDaqClientEvent.FrameReceived(frame));
        }

        public void PublishFault(string errorMessage)
        {
            events.OnNext(new LiveDaqClientEvent.Faulted(errorMessage));
        }

        public void PublishDisconnectedEvent(string? errorMessage)
        {
            events.OnNext(new LiveDaqClientEvent.Disconnected(errorMessage));
        }

        public ValueTask DisposeAsync()
        {
            disposeStarted.TrySetResult();
            if (BlockDisposeAsync)
            {
                return new ValueTask(WaitForDisposeReleaseAsync());
            }

            if (IsConnected)
            {
                IsConnected = false;
                PublishDisconnected();
            }

            events.OnCompleted();
            return ValueTask.CompletedTask;
        }

        public void ReleaseDispose()
        {
            disposeRelease.TrySetResult();
        }

        public void ReleasePendingDisconnectEvents()
        {
            while (pendingDisconnectEvents > 0)
            {
                pendingDisconnectEvents--;
                events.OnNext(new LiveDaqClientEvent.Disconnected(null));
            }

            events.OnNext(new LiveDaqClientEvent.FrameReceived(
                new LiveStartAckFrame(
                    new LiveFrameMetadata(0),
                    new LiveStartAck(LiveStartErrorCode.Ok, DisconnectFlushMarkerSessionId, LiveStreamMask.None))));
        }

        private void PublishDisconnected()
        {
            if (DelayDisconnectEvents)
            {
                pendingDisconnectEvents++;
                return;
            }

            events.OnNext(new LiveDaqClientEvent.Disconnected(null));
        }

        private async Task WaitForDisposeReleaseAsync()
        {
            await disposeRelease.Task;

            if (IsConnected)
            {
                IsConnected = false;
                PublishDisconnected();
            }

            events.OnCompleted();
        }
    }

    public enum CanceledOperation
    {
        Connect,
        Stop,
        ApplyConfiguration
    }
}
