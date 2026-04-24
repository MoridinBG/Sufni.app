using System.Reactive.Subjects;
using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveDaqSharedStreamTests
{
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
    public async Task DisposingLastObserverLease_DisposesClient_AndEvictsStream()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync();

        var firstClient = clientFactory.CreatedClients.Single();

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
        await stream.EnsureStartedAsync();

        var client = clientFactory.CreatedClients.Single();

        await stream.DisposeAsync();
        await statesCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await framesCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        await stream.EnsureStartedAsync();

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = stream.States.Subscribe(state =>
        {
            if (state.IsClosed)
            {
                closed.TrySetResult();
            }
        });

        catalogEntries.OnNext([]);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));

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
        await stream.EnsureStartedAsync();

        var client = clientFactory.CreatedClients.Single();
        client.FailNextStartPreview = true;

        await stream.ApplyConfigurationAsync(new LiveDaqStreamConfiguration(
            SensorMask: LiveSensorMask.Travel | LiveSensorMask.Gps,
            TravelHz: 100,
            ImuHz: 0,
            GpsFixHz: 5));

        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);

        await stream.EnsureStartedAsync();
        await Task.Yield();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task ReconfigureRejection_DoesNotCloseStream_WhenTwoDeliberateDisconnectsArePending()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync();

        var client = clientFactory.CreatedClients.Single();
        client.DelayDisconnectEvents = true;
        client.RejectNextStartPreview = true;

        var markerObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = stream.Frames.Subscribe(frame =>
        {
            if (frame is LiveStartAckFrame startAck && startAck.Payload.SessionId == FakeLiveDaqClient.DisconnectFlushMarkerSessionId)
            {
                markerObserved.TrySetResult();
            }
        });

        await stream.ApplyConfigurationAsync(new LiveDaqStreamConfiguration(
            SensorMask: LiveSensorMask.Travel | LiveSensorMask.Gps,
            TravelHz: 100,
            ImuHz: 0,
            GpsFixHz: 5));

        client.ReleasePendingDisconnectEvents();
        await markerObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Disconnected, stream.CurrentState.ConnectionState);

        await stream.EnsureStartedAsync();

        Assert.False(stream.CurrentState.IsClosed);
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);
    }

    [Fact]
    public async Task EnsureStartedAsync_WhenConnectThrowsCanceled_DoesNotPublishError()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        clientFactory.ConfigureBeforeReturn = c => c.ThrowCanceledOnConnect = true;

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();

        var result = await stream.EnsureStartedAsync();

        Assert.Null(result);
        Assert.Null(stream.CurrentState.LastError);
        Assert.False(stream.CurrentState.IsClosed);
    }

    [Fact]
    public async Task StopAsync_WhenDisconnectThrowsCanceled_DoesNotPublishError()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync();
        Assert.Equal(LiveConnectionState.Connected, stream.CurrentState.ConnectionState);

        var client = clientFactory.CreatedClients.Single();
        client.ThrowCanceledOnDisconnect = true;

        await stream.StopAsync();

        Assert.Null(stream.CurrentState.LastError);
        Assert.False(stream.CurrentState.IsClosed);
    }

    [Fact]
    public async Task ApplyConfigurationAsync_WhenDisconnectThrowsCanceled_DoesNotPublishError()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync();

        var client = clientFactory.CreatedClients.Single();
        client.ThrowCanceledOnDisconnect = true;

        await stream.ApplyConfigurationAsync(new LiveDaqStreamConfiguration(
            SensorMask: LiveSensorMask.Travel | LiveSensorMask.Gps,
            TravelHz: 100,
            ImuHz: 0,
            GpsFixHz: 5));

        Assert.Null(stream.CurrentState.LastError);
        Assert.False(stream.CurrentState.IsClosed);
    }

    [Fact]
    public async Task GetOrCreate_ReturnsReplacementStream_WhenExistingStreamIsPendingEviction()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var first = registry.GetOrCreate(snapshot);
        var firstLease = first.AcquireLease();
        await first.EnsureStartedAsync();

        var firstClient = clientFactory.CreatedClients.Single();
        firstClient.BlockDisposeAsync = true;

        var releaseTask = firstLease.DisposeAsync().AsTask();
        await firstClient.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));

        var replacement = registry.GetOrCreate(snapshot);

        firstClient.ReleaseDispose();
        await releaseTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotSame(first, replacement);

        var again = registry.GetOrCreate(snapshot);
        Assert.Same(replacement, again);
    }

    [Fact]
    public async Task AcquireLease_RescuesPendingEviction_WhenCallerAlreadyHoldsStreamReference()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        var firstLease = stream.AcquireLease();
        await stream.EnsureStartedAsync();

        var client = clientFactory.CreatedClients.Single();
        client.BlockDisposeAsync = true;

        var releaseTask = firstLease.DisposeAsync().AsTask();
        await client.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(2));

        var rescuedLease = stream.AcquireLease();

        client.ReleaseDispose();
        await releaseTask.WaitAsync(TimeSpan.FromSeconds(2));

        var again = registry.GetOrCreate(snapshot);
        Assert.Same(stream, again);

        await rescuedLease.DisposeAsync();
    }

    [Fact]
    public async Task Frames_SlowSubscriber_DoesNotBlockPublishing_AndDropsOldestBufferedFrames()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await using var lease = stream.AcquireLease();
        await stream.EnsureStartedAsync();

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

        const int publishedFrameCount = 1500;
        var publishTask = Task.Run(() =>
        {
            for (var index = 1; index <= publishedFrameCount; index++)
            {
                client.PublishFrame(CreateTravelBatchFrame((ulong)index));
            }
        });

        await firstFrameEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publishTask.WaitAsync(TimeSpan.FromSeconds(2));

        releaseFirstFrame.TrySetResult();

        await AssertEventuallyAsync(() =>
        {
            lock (receivedOffsets)
            {
                return receivedOffsets.Count > 0 && receivedOffsets.Contains((ulong)publishedFrameCount);
            }
        });

        lock (receivedOffsets)
        {
            Assert.DoesNotContain(2UL, receivedOffsets);
            Assert.True(receivedOffsets.Count < publishedFrameCount);
            Assert.Contains((ulong)publishedFrameCount, receivedOffsets);
        }
    }

    private LiveDaqSharedStreamRegistry CreateRegistry() =>
        new(clientFactory, catalogService);

    private static LiveDaqSnapshot CreateSnapshot(string identityKey, string host, int port) =>
        new(
            IdentityKey: identityKey,
            DisplayName: identityKey,
            BoardId: identityKey,
            Host: host,
            Port: port,
            IsOnline: true,
            SetupName: "setup",
            BikeName: "bike");

    private static LiveDaqCatalogEntry CreateCatalogEntry(LiveDaqSnapshot snapshot) =>
        new(
            snapshot.IdentityKey,
            snapshot.DisplayName,
            snapshot.BoardId,
            snapshot.Host!,
            snapshot.Port!.Value);

    private static LiveTravelBatchFrame CreateTravelBatchFrame(ulong firstMonotonicUs) =>
        new(
            new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.TravelBatch, 0, 0),
            new LiveBatchHeader(901, 0, 0, firstMonotonicUs, 1),
            [new LiveTravelRecord((ushort)firstMonotonicUs, (ushort)firstMonotonicUs)]);

    private static async Task AssertEventuallyAsync(Func<bool> predicate)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class FakeLiveDaqClientFactory : ILiveDaqClientFactory
    {
        public List<FakeLiveDaqClient> CreatedClients { get; } = [];

        public Action<FakeLiveDaqClient>? ConfigureBeforeReturn { get; set; }

        public ILiveDaqClient CreateClient()
        {
            var client = new FakeLiveDaqClient();
            ConfigureBeforeReturn?.Invoke(client);
            CreatedClients.Add(client);
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

        public bool RejectNextStartPreview { get; set; }

        public bool DelayDisconnectEvents { get; set; }

        public bool BlockDisposeAsync { get; set; }

        public bool ThrowCanceledOnConnect { get; set; }

        public bool ThrowCanceledOnStartPreview { get; set; }

        public bool ThrowCanceledOnDisconnect { get; set; }

        public bool IsConnected { get; private set; }

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public IObservable<LiveDaqClientEvent> Events => events;

        public Task DisposeStarted => disposeStarted.Task;

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            if (ThrowCanceledOnConnect)
            {
                ThrowCanceledOnConnect = false;
                throw new OperationCanceledException();
            }

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowCanceledOnStartPreview)
            {
                ThrowCanceledOnStartPreview = false;
                throw new OperationCanceledException();
            }

            if (FailNextStartPreview)
            {
                FailNextStartPreview = false;
                return Task.FromResult<LivePreviewStartResult>(new LivePreviewStartResult.Failed("preview start failed"));
            }

            if (RejectNextStartPreview)
            {
                RejectNextStartPreview = false;
                return Task.FromResult<LivePreviewStartResult>(
                    new LivePreviewStartResult.Rejected(LiveStartErrorCode.Busy, LiveStartErrorCode.Busy.ToUserMessage()));
            }

            var header = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: (uint)(900 + ConnectCalls));
            events.OnNext(new LiveDaqClientEvent.FrameReceived(
                new LiveStartAckFrame(
                    new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.StartLiveAck, 0, 0),
                    new LiveStartAck(LiveStartErrorCode.Ok, header.SessionId, request.SensorMask))));
            events.OnNext(new LiveDaqClientEvent.FrameReceived(
                new LiveSessionHeaderFrame(
                    new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.SessionHeader, 0, 0),
                    header)));

            return Task.FromResult<LivePreviewStartResult>(new LivePreviewStartResult.Started(header, request.SensorMask));
        }

        public Task StopPreviewAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCalls++;
            if (ThrowCanceledOnDisconnect)
            {
                ThrowCanceledOnDisconnect = false;
                throw new OperationCanceledException();
            }

            IsConnected = false;
            PublishDisconnected();
            return Task.CompletedTask;
        }

        public void PublishFrame(LiveProtocolFrame frame)
        {
            events.OnNext(new LiveDaqClientEvent.FrameReceived(frame));
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
                    new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.StartLiveAck, 0, 0),
                    new LiveStartAck(LiveStartErrorCode.Ok, DisconnectFlushMarkerSessionId, LiveSensorMask.None))));
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
}