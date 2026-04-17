using System.Reactive.Subjects;
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
        private readonly Subject<LiveDaqClientEvent> events = new();

        public bool FailNextStartPreview { get; set; }

        public bool ThrowCanceledOnConnect { get; set; }

        public bool ThrowCanceledOnStartPreview { get; set; }

        public bool ThrowCanceledOnDisconnect { get; set; }

        public bool IsConnected { get; private set; }

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public IObservable<LiveDaqClientEvent> Events => events;

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
            events.OnNext(new LiveDaqClientEvent.Disconnected(null));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            if (IsConnected)
            {
                IsConnected = false;
                events.OnNext(new LiveDaqClientEvent.Disconnected(null));
            }

            events.OnCompleted();
            return ValueTask.CompletedTask;
        }
    }
}