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
    public async Task DetachLastDiagnosticsObserver_DisposesClient_AndEvictsStream()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await stream.AttachDiagnosticsAsync();
        await stream.EnsureStartedAsync();

        var firstClient = clientFactory.CreatedClients.Single();

        await stream.DetachDiagnosticsAsync();

        Assert.Equal(1, firstClient.DisposeCalls);
        var replacement = registry.GetOrCreate(snapshot);
        Assert.NotSame(stream, replacement);
    }

    [Fact]
    public async Task SessionObserver_LocksConfiguration_UntilDetached()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        var stream = registry.GetOrCreate(snapshot);

        await stream.AttachDiagnosticsAsync();
        Assert.False(stream.CurrentState.IsConfigurationLocked);

        await stream.AttachSessionAsync();
        Assert.True(stream.CurrentState.IsConfigurationLocked);

        await stream.DetachSessionAsync();
        Assert.False(stream.CurrentState.IsConfigurationLocked);

        await stream.DetachDiagnosticsAsync();
    }

    [Fact]
    public async Task CatalogRemoval_ClosesAndEvictsActiveStream()
    {
        using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board-1", "192.168.0.50", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);

        var stream = registry.GetOrCreate(snapshot);
        await stream.AttachDiagnosticsAsync();
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

        public ILiveDaqClient CreateClient()
        {
            var client = new FakeLiveDaqClient();
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class FakeLiveDaqClient : ILiveDaqClient
    {
        private readonly Subject<LiveDaqClientEvent> events = new();

        public bool IsConnected { get; private set; }

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public IObservable<LiveDaqClientEvent> Events => events;

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default)
        {
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