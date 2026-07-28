using System.Reactive.Subjects;
using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqSharedStreamTests
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
        catalogService.AcquireBrowse().Returns(browseLease);
    }

    [Fact]
    public void GetOrCreate_ReusesStreamPerIdentity_AndSeparatesDifferentBoards()
    {
        using var registry = CreateRegistry();
        var firstSnapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        var secondSnapshot = CreateSnapshot("board-2", "192.168.0.51", 1666);

        var first = registry.GetOrCreate(firstSnapshot);
        var again = registry.GetOrCreate(firstSnapshot);
        var other = registry.GetOrCreate(secondSnapshot);

        Assert.Same(first, again);
        Assert.NotSame(first, other);
    }

    [Fact]
    public async Task CurrentState_UsesSnapshotProtocolVersion_AndUpdatesFromCatalog()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557, LiveProtocolVersion.V3);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);

        Assert.Equal(LiveProtocolVersion.V3, stream.CurrentState.ProtocolVersion);

        catalogEntries.OnNext([CreateCatalogEntry(snapshot with { ProtocolVersion = LiveProtocolVersion.V2 })]);

        await AssertEventuallyAsync(() => stream.CurrentState.ProtocolVersion == LiveProtocolVersion.V2);
    }

    [Fact]
    public async Task CatalogProtocolChange_ClosesAndEvictsActiveStream()
    {
        using var registry = CreateRegistry();
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
        Assert.Equal("DAQ protocol changed. Reopen the live tab.", stream.CurrentState.LastError);

        var replacement = registry.GetOrCreate(snapshot with { ProtocolVersion = LiveProtocolVersion.V3 });
        Assert.NotSame(stream, replacement);
    }

    [Fact]
    public async Task DisposingLastObserverLease_DisposesClient_AndEvictsStream()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        var firstClient = clientFactory.CreatedClients.Single();
        Assert.Equal(LiveProtocolVersion.V2, clientFactory.CreatedForSnapshots.Single().ProtocolVersion);

        await lease.DisposeAsync();

        Assert.Equal(1, firstClient.DisposeCalls);
        var replacement = registry.GetOrCreate(snapshot);
        Assert.NotSame(stream, replacement);
    }

    [Fact]
    public async Task ConfigurationLockLease_LocksConfiguration_UntilDisposed()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        var stream = registry.GetOrCreate(snapshot);

        await using var observerLease = stream.AcquireLease();
        Assert.False(stream.CurrentState.IsConfigurationLocked);

        await using var configurationLock = stream.AcquireConfigurationLock();
        Assert.True(stream.CurrentState.IsConfigurationLocked);

        await configurationLock.DisposeAsync();
        Assert.False(stream.CurrentState.IsConfigurationLocked);
    }

    [Fact]
    public async Task DisposeAsync_CompletesObservables_DisposesClient_AndFutureLeaseAcquisitionThrows()
    {
        using var registry = CreateRegistry();
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
        var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        var client = clientFactory.CreatedClients.Single();

        await stream.DisposeAsync();
        await statesCompleted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await framesCompleted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stream.DisposeAsync();

        Assert.Equal(1, client.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => stream.AcquireLease());

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task CatalogRemoval_ClosesAndEvictsActiveStream()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

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
        var replacement = registry.GetOrCreate(snapshot);
        Assert.NotSame(stream, replacement);
    }

    [Fact]
    public async Task ReconfigureFailure_LeavesStreamRecoverableInsteadOfClosed()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        clientFactory.ConfigureBeforeReturn = client => client.FailNextStartPreview = true;

        await stream.ApplyConfigurationAsync(LiveDaqStreamConfiguration.FromRequestedRates(100, 0, 5), cancellationToken: TestContext.Current.CancellationToken);

        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);

        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task EnsureStartedAsync_SharesPendingStart_AndCallerCancellationDoesNotCancelTransport()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingStartCompletion = pendingStart;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        using var canceledWaiter = new CancellationTokenSource();

        var firstWait = stream.EnsureStartedAsync(canceledWaiter.Token);
        var client = clientFactory.CreatedClients.Single();
        await client.StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var secondWait = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        canceledWaiter.Cancel();
        Assert.Null(await firstWait);
        Assert.False(secondWait.IsCompleted);
        Assert.Equal(1, client.StartCalls);

        var acceptedMask = LiveStreamMask.Temperature | LiveStreamMask.Marker;
        pendingStart.SetResult(CreateStartedResult(901, acceptedMask));

        var started = Assert.IsType<LivePreviewStartResult.Started>(await secondWait);
        Assert.Equal(acceptedMask, started.Header.AcceptedStreamMask);
        Assert.Equal(acceptedMask, stream.CurrentState.SelectedStreamMask);
        Assert.Equal(1, client.StartCalls);
    }

    [Fact]
    public async Task ApplyConfigurationAsync_DuringPendingStart_ConvergesToLatestConfiguration()
    {
        using var registry = CreateRegistry();
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
                client.AcceptedStreamMask = LiveStreamMask.Temperature | LiveStreamMask.Marker;
            }
        };

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        var initialStart = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var firstClient = clientFactory.CreatedClients.Single();
        await firstClient.StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var latestConfiguration = new LiveDaqStreamConfiguration(
            RequestedStreamMask: LiveStreamMask.Temperature | LiveStreamMask.Marker,
            RequestedSensorMask: LiveSensorInstanceMask.None,
            TravelRateMhz: 0,
            ImuRateMhz: 0,
            GpsRateMhz: 0,
            TemperatureRateMhz: 5_000);
        var reconfigure = stream.ApplyConfigurationAsync(latestConfiguration, cancellationToken: TestContext.Current.CancellationToken);

        pendingStart.SetResult(CreateStartedResult(902, LiveStreamMask.Travel));
        await initialStart.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await reconfigure.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        var replacement = clientFactory.CreatedClients[1];
        var request = Assert.Single(replacement.StartRequests);
        Assert.Equal(latestConfiguration.RequestedStreamMask, request.RequestedStreamMask);
        Assert.Equal(latestConfiguration.TemperatureRateMhz, request.TemperatureRateMhz);
        Assert.Equal(LiveStreamMask.Temperature | LiveStreamMask.Marker, stream.CurrentState.SelectedStreamMask);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task LifecycleTransportWaits_DoNotHoldSharedGate()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var connectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingConnectCompletion = connectRelease;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        var start = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var client = clientFactory.CreatedClients.Single();
        await client.ConnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var nextConfiguration = LiveDaqStreamConfiguration.FromRequestedRates(100, 20, 5);
        var reconfigure = stream.ApplyConfigurationAsync(nextConfiguration, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(nextConfiguration, stream.RequestedConfiguration);
        await using var concurrentLease = stream.AcquireLease();

        connectRelease.SetResult();
        await start.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await reconfigure.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAsync_DuringPendingStart_InvalidatesLateCompletion_AndPermitsRestart()
    {
        using var registry = CreateRegistry();
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
        await using var lease = stream.AcquireLease();
        var start = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var oldClient = clientFactory.CreatedClients.Single();
        await oldClient.StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var stop = stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
        pendingStart.SetResult(CreateStartedResult(903, LiveStreamMask.Temperature | LiveStreamMask.Marker));
        await start.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);
        Assert.Equal(LiveStreamMask.None, stream.CurrentState.SelectedStreamMask);

        var restarted = await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(restarted);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task CloseAsync_DuringPendingStart_RemainsTerminalAfterLateCompletion()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingStartCompletion = pendingStart;

        var stream = Assert.IsType<LiveDaqSharedStream>(registry.GetOrCreate(snapshot));
        await using var lease = stream.AcquireLease();
        var start = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await clientFactory.CreatedClients.Single().StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await stream.CloseAsync("terminal", cancellationToken: TestContext.Current.CancellationToken);
        pendingStart.SetResult(CreateStartedResult(904, LiveStreamMask.Temperature | LiveStreamMask.Marker));
        Assert.Null(await start);

        Assert.True(stream.CurrentState.IsClosed);
        Assert.Equal("terminal", stream.CurrentState.LastError);
        Assert.Equal(LiveStreamMask.None, stream.CurrentState.SelectedStreamMask);
    }

    [Fact]
    public async Task DisposeAsync_DuringPendingStart_IgnoresLateCompletion()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var pendingStart = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientFactory.ConfigureBeforeReturn = client => client.PendingStartCompletion = pendingStart;

        var stream = registry.GetOrCreate(snapshot);
        var lease = stream.AcquireLease();
        var start = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        await clientFactory.CreatedClients.Single().StartEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await stream.DisposeAsync();
        pendingStart.SetResult(CreateStartedResult(905, LiveStreamMask.Temperature | LiveStreamMask.Marker));
        Assert.Null(await start);
        Assert.Throws<ObjectDisposedException>(() => stream.AcquireLease());

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task OldGenerationEvents_CannotMutateReplacementStateOrPublishFrames()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        clientFactory.ConfigureBeforeReturn = client =>
            client.AcceptedStreamMask = clientFactory.CreatedClients.Count == 0
                ? LiveStreamMask.Travel
                : LiveStreamMask.Temperature | LiveStreamMask.Marker;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var oldClient = clientFactory.CreatedClients.Single();
        var observedFrames = new List<LiveProtocolFrame>();
        using var subscription = stream.Frames.Subscribe(observedFrames.Add);

        var configuration = new LiveDaqStreamConfiguration(
            RequestedStreamMask: LiveStreamMask.Temperature | LiveStreamMask.Marker,
            RequestedSensorMask: LiveSensorInstanceMask.None,
            TravelRateMhz: 0,
            ImuRateMhz: 0,
            GpsRateMhz: 0,
            TemperatureRateMhz: 5_000);
        await stream.ApplyConfigurationAsync(configuration, cancellationToken: TestContext.Current.CancellationToken);

        oldClient.PublishFrame(CreateTravelBatchFrame(77));
        oldClient.PublishFault("stale fault");
        oldClient.PublishDisconnectedEvent("stale disconnect");
        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
        Assert.Equal(LiveStreamMask.Temperature | LiveStreamMask.Marker, stream.CurrentState.SelectedStreamMask);
        Assert.DoesNotContain(observedFrames, frame => frame is LiveTravelBatchFrame travel && travel.Batch.FirstMonotonicUs == 77);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task StopAsync_DoesNotHoldGate_WhileStopOrDisconnectIsPending()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var client = clientFactory.CreatedClients.Single();
        var stopRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingStopCompletion = stopRelease;
        client.PendingDisconnectCompletion = disconnectRelease;

        var stop = stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.StopEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await using var leaseWhileStopping = stream.AcquireLease();

        stopRelease.SetResult();
        await client.DisconnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await using var leaseWhileDisconnecting = stream.AcquireLease();

        disconnectRelease.SetResult();
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task EnsureStartedAsync_DuringPendingStop_ReconvergesToRunning()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var oldClient = clientFactory.CreatedClients.Single();
        var stopRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldClient.PendingStopCompletion = stopRelease;
        oldClient.PendingDisconnectCompletion = disconnectRelease;

        var stop = stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);
        await oldClient.StopEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var restart = stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(restart.IsCompleted);

        stopRelease.SetResult();
        await oldClient.DisconnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        disconnectRelease.SetResult();

        var restarted = await restart.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Started>(restarted);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.Single(clientFactory.CreatedClients[1].StartRequests);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task StopAsync_StopsPreviewBeforeDisconnecting()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
        var client = clientFactory.CreatedClients.Single();

        await stream.StopAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["stop", "disconnect"], client.StopLifecycleCalls);
        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);
    }

    [Theory]
    [InlineData(CanceledOperation.Connect)]
    [InlineData(CanceledOperation.Stop)]
    [InlineData(CanceledOperation.ApplyConfiguration)]
    public async Task Operations_WhenClientThrowsCanceled_DoNotPublishError(CanceledOperation operation)
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        if (operation is CanceledOperation.Connect)
        {
            clientFactory.ConfigureBeforeReturn = c => c.ThrowCanceledOnConnect = true;
        }

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        if (operation is not CanceledOperation.Connect)
        {
            await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
            clientFactory.CreatedClients.Single().ThrowCanceledOnDisconnect = true;
        }

        switch (operation)
        {
            case CanceledOperation.Connect:
                Assert.Null(await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task EnsureStartedAsync_PartialSuccessPublishesConnectedStateWithoutError()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        clientFactory.ConfigureBeforeReturn = c =>
        {
            c.AcceptedSensorMask = LiveSensorInstanceMask.ForkTravel;
        };

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.ApplyConfigurationAsync(LiveDaqStreamConfiguration.FromRequestedRates(100, 0, 0), cancellationToken: TestContext.Current.CancellationToken);

        var result = await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        var started = Assert.IsType<LivePreviewStartResult.Started>(result);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
        Assert.Null(stream.CurrentState.LastError);
        Assert.Equal(LiveSensorInstanceMask.ForkTravel, stream.CurrentState.SessionHeader?.AcceptedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.ShockTravel, started.Header.MissingSensorMask);
    }

    [Fact]
    public async Task EnsureStartedAsync_NoSensorsStartedPublishesError()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        clientFactory.ConfigureBeforeReturn = c =>
        {
            c.RejectNextStartPreview = true;
            c.RejectErrorCode = LiveStartErrorCode.NoSensorsStarted;
        };

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();

        var result = await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.Equal(LiveStartErrorCode.NoSensorsStarted, rejected.ErrorCode);
        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);
        Assert.NotNull(stream.CurrentState.LastError);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task GetOrCreate_ReturnsReplacementStream_WhenExistingStreamIsPendingEviction()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var first = registry.GetOrCreate(snapshot);
        var firstLease = first.AcquireLease();
        await first.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        var firstClient = clientFactory.CreatedClients.Single();
        firstClient.BlockDisposeAsync = true;

        var releaseTask = firstLease.DisposeAsync().AsTask();
        await firstClient.DisposeStarted.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var replacement = registry.GetOrCreate(snapshot);

        firstClient.ReleaseDispose();
        await releaseTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.NotSame(first, replacement);

        var again = registry.GetOrCreate(snapshot);
        Assert.Same(replacement, again);
    }

    [Fact(Timeout = DeadlockProneTestTimeoutMs)]
    public async Task AcquireLease_RescuesPendingEviction_WhenCallerAlreadyHoldsStreamReference()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        var firstLease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

        var client = clientFactory.CreatedClients.Single();
        client.BlockDisposeAsync = true;

        var releaseTask = firstLease.DisposeAsync().AsTask();
        await client.DisposeStarted.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var rescuedLease = stream.AcquireLease();

        client.ReleaseDispose();
        await releaseTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var again = registry.GetOrCreate(snapshot);
        Assert.Same(stream, again);

        await rescuedLease.DisposeAsync();
    }

    [Fact(Timeout = SlowSubscriberTestTimeoutMs)]
    public async Task Frames_SlowSubscriber_DoesNotBlockPublishing_AndDropsOldestBufferedFrames()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync(cancellationToken: TestContext.Current.CancellationToken);

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
            snapshot.Host!,
            snapshot.Port!.Value,
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

        public int DisconnectCalls { get; private set; }

        public List<string> StopLifecycleCalls { get; } = [];

        public int DisposeCalls { get; private set; }

        public IObservable<LiveDaqClientEvent> Events => events;

        public Task DisposeStarted => disposeStarted.Task;

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
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
                return new LivePreviewStartResult.Failed("preview start failed");
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
            DisposeCalls++;
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
