using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Models;
using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
using Sufni.App.Video.Stores;

namespace Sufni.App.Tests.Shell.Coordinators;

public class AppDataRefresherTests
{
    [Fact]
    public async Task RefreshAsync_WaitsForAllReads_BeforeOneCoherentReplacement()
    {
        using var fixture = new Fixture();
        var oldBike = new Bike(Guid.NewGuid(), "removed");
        await fixture.Bikes.ReplaceAllAsync([BikeSnapshot.From(oldBike)]);
        var lastRead = new TaskCompletionSource<List<SessionSegment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedLastRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SegmentRepository.GetLiveAsync().Returns(_ =>
        {
            reachedLastRead.TrySetResult();
            return lastRead.Task;
        });
        var observed = new List<(string?, Guid?, string?, string?, string?, string?, string?, bool, TrackSnapshot?, BoardSnapshot?)>();
        using var subscription = fixture.Publication.Committed.Subscribe(_ => observed.Add((
            fixture.Bikes.Get(fixture.Bike.Id)?.Name,
            fixture.Setups.Get(fixture.Setup.Id)?.Id,
            fixture.Sessions.Get(fixture.Session.Id)?.Name,
            fixture.Sources.Get(fixture.Session.Id)?.SourceHash,
            fixture.Devices.Get("phone")?.DisplayName,
            fixture.Videos.Get(fixture.Clip.Id)?.Path,
            fixture.Segments.Get(fixture.Segment.Id)?.Name,
            fixture.Bikes.Get(oldBike.Id) is null,
            fixture.Tracks.Get(fixture.Track.Id), fixture.Boards.Get(fixture.Board.Id))));

        var refresh = fixture.Refresher.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Empty(observed);
        await reachedLastRead.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(observed);
        Assert.NotNull(fixture.Bikes.Get(oldBike.Id));
        lastRead.SetResult([fixture.Segment]);
        await refresh.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(("bike", fixture.Setup.Id, "session", "source hash", "Phone", "", "segment", true, fixture.Track, BoardSnapshot.From(fixture.Board)),
            Assert.Single(observed));
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentCallersShareThePendingReadAndPublication()
    {
        using var fixture = new Fixture();
        var lastRead = new TaskCompletionSource<List<SessionSegment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SegmentRepository.GetLiveAsync().Returns(lastRead.Task);
        var observed = new List<string?>();
        using var subscription = fixture.Publication.Committed.Subscribe(_ =>
            observed.Add(fixture.Segments.Get(fixture.Segment.Id)?.Name));

        var first = fixture.Refresher.RefreshAsync(TestContext.Current.CancellationToken);
        var second = fixture.Refresher.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        lastRead.SetResult([fixture.Segment]);
        await Task.WhenAll(first, second).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["segment"], observed);
        await fixture.SegmentRepository.Received(1).GetLiveAsync();
    }

    [Fact]
    public async Task RefreshAsync_CancelledReadKeepsPreviousState_AndNextRefreshCanPublish()
    {
        using var fixture = new Fixture();
        await fixture.Bikes.ReplaceAllAsync([BikeSnapshot.From(fixture.Bike) with { Name = "previous" }]);
        var lastRead = new TaskCompletionSource<List<SessionSegment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SegmentRepository.GetLiveAsync().Returns(lastRead.Task);
        var observed = new List<string?>();
        using var subscription = fixture.Publication.Committed.Subscribe(_ =>
            observed.Add(fixture.Bikes.Get(fixture.Bike.Id)?.Name));
        using var cancellation = new CancellationTokenSource();

        var refresh = fixture.Refresher.RefreshAsync(cancellation.Token);
        cancellation.Cancel();
        lastRead.SetResult([fixture.Segment]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            refresh.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));

        Assert.Empty(observed);
        Assert.Equal("previous", fixture.Bikes.Get(fixture.Bike.Id)?.Name);
        await fixture.Refresher.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["bike"], observed);
    }

    private sealed class Fixture : IDisposable
    {
        public TrackSnapshot Track { get; } = new(Guid.NewGuid(), 100.25, 101.75, 3);
        public TrackStore Tracks { get; }
        public BoardStore Boards { get; }
        public Bike Bike { get; } = new(Guid.NewGuid(), "bike");
        public Setup Setup { get; }
        public Board Board { get; }
        public Session Session { get; }
        public SessionVideoClip Clip { get; }
        public SessionSegment Segment { get; }
        public ISessionSegmentRepository SegmentRepository { get; } = Substitute.For<ISessionSegmentRepository>();
        public StorePublication Publication { get; }
        public BikeStore Bikes { get; }
        public SetupStore Setups { get; }
        public SessionStore Sessions { get; }
        public RecordedSessionSourceStore Sources { get; }
        public PairedDeviceStore Devices { get; }
        public SessionVideoStore Videos { get; }
        public SessionSegmentStore Segments { get; }
        public AppDataRefresher Refresher { get; }

        public Fixture()
        {
            Setup = new Setup(Guid.NewGuid(), "setup") { BikeId = Bike.Id };
            Board = new Board(Guid.NewGuid(), Setup.Id);
            Session = new Session(Guid.NewGuid(), "session", "", Setup.Id);
            Clip = new SessionVideoClip { Id = Guid.NewGuid(), OwnerKey = "source-hash", Path = "" };
            Segment = new SessionSegment { Id = Guid.NewGuid(), OwnerKey = "source-hash", Name = "segment" };
            var trackRepository = Substitute.For<ITrackRepository>();
            trackRepository.GetTrackSnapshotsAsync().Returns([Track]);
            var bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
            var setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
            var boardRepository = Substitute.For<ISynchronizableRepository<Board>>();
            var sessionRepository = Substitute.For<ISessionRepository>();
            var sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
            var deviceRepository = Substitute.For<IPairedDeviceRepository>();
            var videoRepository = Substitute.For<ISessionVideoRepository>();
            bikeRepository.GetAllAsync().Returns([Bike]);
            setupRepository.GetAllAsync().Returns([Setup]);
            boardRepository.GetAllAsync().Returns([Board]);
            sessionRepository.GetSessionsAsync().Returns([Session]);
            sourceRepository.GetRecordedSessionSourceSnapshotsAsync().Returns(
                [new RecordedSessionSourceSnapshot(Session.Id, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, "source hash")]);
            deviceRepository.GetPairedDevicesAsync().Returns([new PairedDevice("phone", "Phone", new DateTime(2030, 1, 1))]);
            videoRepository.GetLiveAsync().Returns([Clip]);
            SegmentRepository.GetLiveAsync().Returns([Segment]);
            var dispatcher = new InlineUiThreadDispatcher();
            Publication = new StorePublication(dispatcher);
            Tracks = new TrackStore(Publication);
            Boards = new BoardStore(Publication);
            Bikes = new BikeStore(Publication);
            Setups = new SetupStore(Publication);
            Sessions = new SessionStore(Publication);
            Sources = new RecordedSessionSourceStore(Publication);
            Devices = new PairedDeviceStore(Publication);
            Videos = new SessionVideoStore(Publication);
            Segments = new SessionSegmentStore(Publication);
            Refresher = new AppDataRefresher(Bikes, Setups, Sessions, Sources, Devices, Videos, Segments,
                bikeRepository, setupRepository, boardRepository, sessionRepository, sourceRepository,
                deviceRepository, videoRepository, SegmentRepository, new InlineBackgroundTaskRunner(),
                dispatcher, Publication, Tracks, trackRepository, Boards);
        }

        public void Dispose()
        {
            Tracks.Dispose();
            Boards.Dispose();
            Bikes.Dispose();
            Setups.Dispose();
            Sessions.Dispose();
            Sources.Dispose();
            Devices.Dispose();
            Videos.Dispose();
            Segments.Dispose();
            Publication.Dispose();
        }
    }
}
