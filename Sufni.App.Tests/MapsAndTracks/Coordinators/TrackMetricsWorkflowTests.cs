using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Queries;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.MapsAndTracks.Coordinators;

public class TrackMetricsWorkflowTests
{
    [Fact]
    public async Task EmptyProjectedWindow_ReplacesOldMetricsWithAnEmptyCurrentMaterialization()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Reader.GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TrackPoint>)Array.Empty<TrackPoint>());
        using var owner = fixture.Start();
        await owner.StopAsync();

        Assert.NotNull(fixture.Current.GpsMetricsKey);
        Assert.Null(fixture.Current.DistanceMeters);
        Assert.Null(fixture.Current.AscentMeters);
        Assert.Null(fixture.Current.DescentMeters);
    }

    [Fact]
    public async Task StopAsync_DrainsPendingInputEvaluationWithoutStartingMetricReads()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var runner = new DeferredBackgroundTaskRunner();
        using var owner = fixture.Start(runner);

        var stopped = owner.StopAsync();
        Assert.False(stopped.IsCompleted);
        await runner.RunNextAsync();
        await stopped.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(fixture.Session, fixture.Current);
        await fixture.Reader.DidNotReceive().GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommittedAlignmentChange_SupersedesHeldWorkAndRetainsOldMetricsUntilReplacement()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var held = new TaskCompletionSource<IReadOnlyList<TrackPoint>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.GetSessionTrackAsync(Arg.Is<SessionTrackKey>(key => key.GpsOffsetSeconds == 0), Arg.Any<CancellationToken>())
            .Returns(held.Task);
        var originalKey = fixture.CurrentInput();
        using var owner = fixture.Start();
        Assert.Equal(123, fixture.Current.DistanceMeters);
        var replacement = fixture.Sessions.Watch(fixture.Session.Id)
            .FirstAsync(session => session.GpsMetricsKey?.GpsOffsetSeconds == 1).ToTask(TestContext.Current.CancellationToken);

        await fixture.Sessions.PublishSessionsChangedAsync([fixture.Current with { GpsOffsetSeconds = 1 }]);
        var updated = await replacement.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        held.SetResult(Points(100, 100));
        await owner.StopAsync();

        Assert.InRange(updated.DistanceMeters!.Value, 4.98, 5.01);
        Assert.Equal(updated, fixture.Current);
        await fixture.Repository.DidNotReceive().TryCommitGpsMetricsAsync(fixture.Session.Id, originalKey,
            Arg.Any<GpsMetrics>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullerTrackPublication_RecomputesMetricsWithoutAnExplicitCommand()
    {
        using var fixture = new Fixture();
        var partial = fixture.Track with { EndTime = 101 };
        await fixture.SeedAsync(partial);
        using var owner = fixture.Start();
        var first = fixture.Current;
        Assert.Equal(partial.Id, first.GpsMetricsKey!.TrackContentId);
        var fuller = fixture.Track with { Id = Guid.NewGuid() };
        fixture.Reader.GetSessionTrackAsync(Arg.Is<SessionTrackKey>(key => key.TrackId == fuller.Id), Arg.Any<CancellationToken>())
            .Returns(Points(100, 10));
        var replacement = fixture.Sessions.Watch(fixture.Session.Id)
            .FirstAsync(session => session.GpsMetricsKey?.TrackContentId == fuller.Id).ToTask(TestContext.Current.CancellationToken);

        await fixture.Tracks.PublishTracksChangedAsync([fuller]);
        var updated = await replacement.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await owner.StopAsync();

        Assert.InRange(updated.DistanceMeters!.Value, 9.98, 10.01);
        Assert.NotEqual(first.GpsMetricsKey, updated.GpsMetricsKey);
        Assert.Equal(first.Updated, updated.Updated);
    }

    [Fact]
    public async Task Completion_PreservesMetadataAuthoredWhileMetricsWereRunning()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var held = new TaskCompletionSource<IReadOnlyList<TrackPoint>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>()).Returns(held.Task);
        using var owner = fixture.Start();
        await fixture.Sessions.PublishSessionsChangedAsync([fixture.Current with { Name = "edited", Updated = 99 }]);
        var replacement = fixture.Sessions.Watch(fixture.Session.Id)
            .FirstAsync(session => session.GpsMetricsKey is not null).ToTask(TestContext.Current.CancellationToken);

        held.SetResult(Points());
        var updated = await replacement.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await owner.StopAsync();

        Assert.Equal("edited", updated.Name);
        Assert.Equal(99, updated.Updated);
        Assert.Equal(4, updated.AscentMeters);
    }

    [Fact]
    public async Task FailedWrite_KeepsVisibleMetricsAndTheirStaleKey()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Repository.TryCommitGpsMetricsAsync(Arg.Any<Guid>(), Arg.Any<GpsMetricsKey>(), Arg.Any<GpsMetrics>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));
        using var owner = fixture.Start();
        await owner.StopAsync();

        Assert.Equal(fixture.Session, fixture.Current);
        Assert.Null(fixture.Current.GpsMetricsKey);
    }

    [Fact]
    public async Task StopAsync_CancelsAndDrainsHeldWorkAndStopsAcceptingChanges()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var held = new TaskCompletionSource<IReadOnlyList<TrackPoint>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>()).Returns(held.Task);
        using var owner = fixture.Start();

        var stopped = owner.StopAsync();
        Assert.False(stopped.IsCompleted);
        held.SetResult(Points());
        await stopped.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await fixture.Sessions.PublishSessionsChangedAsync([fixture.Current with { GpsOffsetSeconds = 1 }]);

        Assert.Equal(123, fixture.Current.DistanceMeters);
        Assert.Null(fixture.Current.GpsMetricsKey);
        await fixture.Repository.DidNotReceive().TryCommitGpsMetricsAsync(Arg.Any<Guid>(), Arg.Any<GpsMetricsKey>(),
            Arg.Any<GpsMetrics>(), Arg.Any<CancellationToken>());
    }

    private static IReadOnlyList<TrackPoint> Points(double start = 100, double distance = 5) =>
        [new(start, 0, 0, 100), new(start + 2, distance * 0.6, distance * 0.8, 104)];

    private sealed class Fixture : IDisposable
    {
        public SessionSnapshot Session { get; } = TestSnapshots.Session(timestamp: 100, updated: 42) with
        { DurationSeconds = 2, DistanceMeters = 123, AscentMeters = 77, DescentMeters = 88 };
        public TrackSnapshot Track { get; } = new(Guid.NewGuid(), 100, 110, 1);
        public InlineUiThreadDispatcher Dispatcher { get; } = new();
        public StorePublication Publication { get; }
        public SessionStore Sessions { get; }
        public TrackStore Tracks { get; }
        public ITrackRepository Repository { get; } = Substitute.For<ITrackRepository>();
        public ISessionTrackReader Reader { get; } = Substitute.For<ISessionTrackReader>();
        public SessionSnapshot Current => Sessions.Get(Session.Id)!;

        public Fixture()
        {
            Publication = new StorePublication(Dispatcher);
            Sessions = new SessionStore(Publication);
            Tracks = new TrackStore(Publication);
            Reader.GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>()).Returns(Points());
            Repository.TryCommitGpsMetricsAsync(Arg.Any<Guid>(), Arg.Any<GpsMetricsKey>(), Arg.Any<GpsMetrics>(), Arg.Any<CancellationToken>())
                .Returns(true);
        }

        public async Task SeedAsync(TrackSnapshot? track = null)
        {
            using var batch = Publication.BeginBatch();
            await Sessions.ReplaceAllAsync([Session]);
            await Tracks.ReplaceAllAsync([track ?? Track]);
        }

        public GpsMetricsKey CurrentInput() => SessionGpsMetricsQuery.Evaluate(Current.Timestamp, Current.DurationSeconds,
            Current.GpsOffsetSeconds, Current.WindowStartSeconds, Tracks.GetAll()).Key;

        public TrackCoordinator Start(IBackgroundTaskRunner? runner = null) => new(Repository, Substitute.For<ISynchronizableRepository<Track>>(),
            Substitute.For<ISessionRepository>(), Substitute.For<IFilesService>(), runner ?? new InlineBackgroundTaskRunner(),
            Reader, Substitute.For<IFullTrackPointReader>(), Substitute.For<IGpxTrackReader>(), Tracks, Sessions, Dispatcher, Publication);

        public void Dispose()
        {
            Sessions.Dispose();
            Tracks.Dispose();
            Publication.Dispose();
        }
    }
}
