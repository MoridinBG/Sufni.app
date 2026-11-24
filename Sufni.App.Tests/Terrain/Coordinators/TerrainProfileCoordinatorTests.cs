using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using NSubstitute;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Coordinators;

public class TerrainProfileCoordinatorTests
{
    [Fact]
    public async Task BackfillHonorsEditorPriorityAfterTheRunningRecording()
    {
        await using var h = new Harness();
        var newest = Session(102);
        var middle = Session(101);
        var oldest = Session(100);
        await h.StartAsync([middle, oldest, newest]);
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Read = async (id, key, token) =>
        {
            if (id == newest.Id) { started.TrySetResult(); await release.Task.WaitAsync(token); }
            return h.Available(key);
        };
        await h.DrainAsync(started.Task);
        h.Coordinator.Prioritize(oldest.Id);
        await h.Dispatcher.DrainAsync();
        release.SetResult();
        await h.WaitForAsync(middle, TerrainOutcome.Computed);
        Assert.Equal([newest.Id, oldest.Id, middle.Id], h.Published.Select(publication => publication.Id));
        foreach (var session in new[] { oldest, middle, newest })
            Assert.True(Assert.IsType<TerrainStatus.Current>(h.Status(session.Id)).Summary.ClassifiedMetres > 0);
    }

    [Fact]
    public async Task OffsetEditSupersedesAnInFlightReadAndKeepsTheAuthoredName()
    {
        await using var h = new Harness();
        var session = Session();
        var started = new TaskCompletionSource();
        h.Read = async (_, key, token) =>
        {
            if (key.GpsOffsetSeconds == 0)
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return h.Available(key);
        };
        await h.StartAsync([session]);
        await h.DrainAsync(started.Task);
        await h.PublishAsync(session with { GpsOffsetSeconds = 1, Name = "corrected" });
        await h.WaitForAsync(session, TerrainOutcome.Computed);
        Assert.Equal(1, Assert.Single(h.Published).Key.Content.GpsOffsetSeconds);
        Assert.Equal(1, h.Repository.StoredKey(session.Id)!.Content.GpsOffsetSeconds);
        Assert.Equal("corrected", h.Sessions.Get(session.Id)!.Name);
    }

    [Fact]
    public async Task AuthoredEditDuringCommitKeepsItsMetadataAndTheComputedProfile()
    {
        await using var h = new Harness();
        var session = Session();
        var writing = new TaskCompletionSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Repository.BeforeWrite = () =>
        {
            writing.TrySetResult();
            return release.Task;
        };
        await h.StartAsync([session]);
        await h.DrainAsync(writing.Task);
        await h.PublishAsync(session with { Description = "after the ride" });
        release.SetResult();
        await h.WaitForAsync(session, TerrainOutcome.Computed);
        Assert.IsType<TerrainStatus.Current>(h.Status(session.Id));
        Assert.Equal("after the ride", h.Sessions.Get(session.Id)!.Description);
    }

    [Fact]
    public async Task NoOverlapFinishesWithoutReadingTelemetry()
    {
        await using var h = new Harness();
        var session = Session(500);
        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.NoTrack);
        Assert.Equal(new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack), h.Status(session.Id));
        Assert.Empty(h.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingExactContentFinishesUnavailable(bool missingFixes)
    {
        await using var h = new Harness();
        var session = Session();
        if (missingFixes) h.Points.GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sufni.App.MapsAndTracks.Models.TrackPoint>?>(null));
        else h.Read = (_, _, _) => Task.FromResult<SessionContentSnapshotResult>(new SessionContentSnapshotResult.Missing());
        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.ContentMissing);
        Assert.Equal(new TerrainStatus.Unavailable(TerrainAvailabilityReason.ContentMissing), h.Status(session.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureBelongsToOneRecordingKey(bool commitThrows)
    {
        await using var h = new Harness();
        var session = Session();
        if (commitThrows) h.Repository.WriteFailure = new IOException();
        else h.Read = (_, key, _) => Task.FromResult(h.Available(key,
            key.GpsOffsetSeconds == 0 ? new TelemetryData() : null));

        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.Failed);
        Assert.IsType<TerrainStatus.Failed>(h.Status(session.Id));
        h.Repository.WriteFailure = null;
        var other = Session(600);
        await h.PublishAsync(other);
        await h.WaitForAsync(other, TerrainOutcome.NoTrack);
        if (commitThrows)
        {
            await h.PublishAsync(session);
            await h.WaitForAsync(session, TerrainOutcome.Failed);
        }
        Assert.Single(h.Reads);
        await h.PublishAsync(session with { GpsOffsetSeconds = 1 });
        await h.WaitForAsync(session, TerrainOutcome.Computed);
        Assert.IsType<TerrainStatus.Current>(h.Status(session.Id));
        Assert.Equal(2, h.Reads.Count);
    }

    [Fact]
    public async Task StopDrainsAnInterruptedReadWithoutWriting()
    {
        await using var h = new Harness();
        var started = new TaskCompletionSource();
        h.Read = async (_, key, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return h.Available(key);
        };
        var session = Session();
        await h.StartAsync([session]);
        await h.DrainAsync(started.Task);
        await h.DrainAsync(h.Coordinator.StopAsync());
        Assert.Null(h.Repository.StoredKey(session.Id));
        Assert.Empty(h.Published);
    }

    private static SessionSnapshot Session(long timestamp = 100) =>
        TestSnapshots.Session(timestamp: timestamp, hasProcessedData: true) with { DurationSeconds = 12 };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SyntheticRide ride = SyntheticRide.Build([new RideSection(140)]);
        private readonly StorePublication publication;
        private readonly TrackStore tracks;
        private readonly TrackSnapshot track;
        private readonly DeferredBackgroundTaskRunner runner = new();
        private readonly IDisposable terrainPublications;
        public ManualUiThreadDispatcher Dispatcher { get; } = new();
        public SessionStore Sessions { get; }
        public InMemoryTerrainProfileRepository Repository { get; } = new();
        public IFullTrackPointReader Points { get; } = Substitute.For<IFullTrackPointReader>();
        public TerrainProfileCoordinator Coordinator { get; }
        public List<(Guid Id, TerrainProfileKey Key)> Published { get; } = [];
        public List<Guid> Reads { get; } = [];
        public Func<Guid, SessionContentToken, CancellationToken, Task<SessionContentSnapshotResult>> Read { get; set; }

        public Harness()
        {
            publication = new StorePublication(Dispatcher);
            Sessions = new SessionStore(publication);
            tracks = new TrackStore(publication);
            var full = SyntheticRideTracks.FullTrack(ride, new SessionTrackTimeline(100));
            track = full.Track;
            Points.GetTrackPointsExactAsync(track.Id, track.PointsRevision, Arg.Any<CancellationToken>()).Returns(full.Points);
            var content = Substitute.For<ISessionContentReader>();
            Read = (_, key, _) => Task.FromResult(Available(key));
            content.GetExactSnapshotAsync(Arg.Any<Guid>(), Arg.Any<SessionContentToken>(),
                SessionContentSelection.ProcessedTelemetry, Arg.Any<CancellationToken>()).Returns(call =>
            {
                Reads.Add(call.Arg<Guid>());
                return Read(call.Arg<Guid>(), call.Arg<SessionContentToken>(), call.Arg<CancellationToken>());
            });
            terrainPublications = Sessions.Connect().Subscribe(changes => Published.AddRange(changes
                .Where(change => change.Current.TerrainProfileKey is { } key
                    && (!change.Previous.HasValue || change.Previous.Value.TerrainProfileKey != key))
                .Select(change => (change.Current.Id, change.Current.TerrainProfileKey!))));
            Coordinator = new(Sessions, tracks, content, Points, Repository, runner, Dispatcher, publication);
        }

        public SessionContentSnapshotResult Available(SessionContentToken key, TelemetryData? telemetry = null) =>
            new SessionContentSnapshotResult.Available(new(key, SessionContentSelection.ProcessedTelemetry, telemetry ?? ride.Telemetry, null));

        public async Task StartAsync(SessionSnapshot[] sessions)
        {
            var publicationTask = Dispatcher.InvokeAsync(async () =>
            {
                using var batch = publication.BeginBatch();
                await tracks.PublishTracksChangedAsync([track]);
                await Sessions.PublishSessionsChangedAsync(sessions);
            });
            await Dispatcher.DrainUntilAsync(publicationTask);
        }

        public Task PublishAsync(SessionSnapshot session) =>
            Dispatcher.DrainUntilAsync(Sessions.PublishSessionsChangedAsync([session]));

        public TerrainStatus Status(Guid id) => SessionTerrainStatusQuery.Evaluate(Sessions.Get(id)!, tracks.GetAll()).Status;

        public Task DrainAsync(Task task) => runner.DrainUntilAsync(Dispatcher, task);

        public Task WaitForAsync(SessionSnapshot session, TerrainOutcome outcome) => DrainAsync(Sessions.Watch(session.Id)
            .Where(value => value.TerrainOutcome == outcome).Take(1).ToTask(TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync()
        {
            await DrainAsync(Coordinator.StopAsync());
            Coordinator.Dispose();
            terrainPublications.Dispose();
            tracks.Dispose();
            Sessions.Dispose();
            publication.Dispose();
        }
    }
}
