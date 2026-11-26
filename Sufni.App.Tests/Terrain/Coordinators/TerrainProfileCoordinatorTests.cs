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
using Sufni.App.Terrain.Stores;
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
        await h.Dispatcher.DrainUntilAsync(h.Dispatcher.InvokeAsync(() => h.Coordinator.Prioritize(oldest.Id)));
        release.SetResult();
        await h.WaitForAsync(middle, TerrainOutcome.Computed);
        Assert.Equal([newest.Id, oldest.Id, middle.Id], h.Published.Select(publication => publication.Id));
        foreach (var session in new[] { oldest, middle, newest })
            Assert.True(Assert.IsType<TerrainStatus.Current>(h.Status(session.Id)).Summary.MovingMetres > 0);
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
    public async Task ProfileCommitClearsTheClassesAndTheLabelSummaryOfTheEarlierProfile()
    {
        await using var h = new Harness();
        var earlier = TerrainProfileKey.From(SessionContentToken.Create(Session(), [], out _));
        var session = Session() with
        {
            TerrainClassKey = new(earlier, TerrainProfiles.Scale, TerrainLabelSet.None, TerrainClassifier.Version),
            TerrainClassSummary = new(400, 0.25),
            TerrainLabelKey = new(earlier, TerrainLabelSet.None, TerrainLabels.Version),
            TerrainLabelSummary = new(null, null, null, null, null)
        };
        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.Computed);
        var published = h.Sessions.Get(session.Id)!;
        Assert.Equal((null, null), (published.TerrainClassKey, published.TerrainClassSummary));
        Assert.Equal((null, null), (published.TerrainLabelKey, published.TerrainLabelSummary));
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

    [Fact]
    public async Task MissingTrackFixesFinishAsContentMissing()
    {
        await using var h = new Harness();
        var session = Session();
        h.Points.GetTrackPointsExactAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sufni.App.MapsAndTracks.Models.TrackPoint>?>(null));
        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.ContentMissing);
        Assert.Equal(new TerrainStatus.Unavailable(TerrainAvailabilityReason.ContentMissing), h.Status(session.Id));
    }

    [Fact]
    public async Task MissingTelemetryEndsTheRunWithoutAWrite()
    {
        await using var h = new Harness();
        var session = Session();
        var read = new TaskCompletionSource();
        h.Read = (_, _, _) =>
        {
            read.TrySetResult();
            return Task.FromResult<SessionContentSnapshotResult>(new SessionContentSnapshotResult.Missing());
        };
        await h.StartAsync([session]);
        await h.DrainAsync(read.Task);
        var next = Session(500);
        await h.PublishAsync(next);
        await h.WaitForAsync(next, TerrainOutcome.NoTrack);
        Assert.Null(h.Repository.StoredKey(session.Id));
        Assert.IsType<TerrainStatus.Pending>(h.Status(session.Id));
    }

    [Fact]
    public async Task FailedCalculationRunsAgainOnlyForANewKey()
    {
        await using var h = new Harness();
        var session = Session();
        var attempts = 0;
        h.Read = (_, key, _) => Task.FromResult(h.Available(key,
            Interlocked.Increment(ref attempts) == 1 ? new TelemetryData() : null));
        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.Failed);
        var other = Session(600);
        await h.PublishAsync(other);
        await h.WaitForAsync(other, TerrainOutcome.NoTrack);
        Assert.IsType<TerrainStatus.Failed>(h.Status(session.Id));
        await h.PublishAsync(session with { GpsOffsetSeconds = 1 });
        await h.WaitForAsync(session, TerrainOutcome.Computed);
        Assert.IsType<TerrainStatus.Current>(h.Status(session.Id));
    }

    [Fact]
    public async Task ThrowingCommitPublishesFailedWithoutAStoredRow()
    {
        await using var h = new Harness();
        var session = Session();
        h.Repository.WriteFailure = new IOException();
        await h.StartAsync([session]);
        await h.WaitForAsync(session, TerrainOutcome.Failed);
        Assert.IsType<TerrainStatus.Failed>(h.Status(session.Id));
        Assert.Null(h.Repository.StoredKey(session.Id));
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

    [Fact]
    public async Task ClassWorkWaitsForProfileWorkAndPublishesTheSetupInOneStep()
    {
        await using var h = new Harness();
        var setup = Guid.NewGuid();
        var measured = h.ReadySetup(setup);
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Read = async (_, key, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return h.Available(key);
        };
        await h.StartAsync([.. measured, Session()]);
        await h.DrainAsync(started.Task);
        Assert.Empty(h.ClassPublications);

        release.SetResult();
        await h.WaitForClassesAsync(measured[0], ScaleAt(0));
        Assert.Equal(measured.Select(session => session.Id).Order(), Assert.Single(h.ClassPublications).Order());
        Assert.All(measured, session => Assert.Equal(new TerrainClassSummary(2 * TerrainProfileCalculator.WindowMetres, 1),
            h.Sessions.Get(session.Id)!.TerrainClassSummary));
    }

    [Fact]
    public async Task ChangedScaleKeyReclassesEverySessionOfTheSetupInOneStep()
    {
        await using var h = new Harness();
        var setup = Guid.NewGuid();
        var measured = h.ReadySetup(setup);
        await h.StartAsync(measured);
        await h.WaitForClassesAsync(measured[0], ScaleAt(0));

        var rougher = h.Measured(setup, 104, bin: TwoStepsUp, metres: 10000);
        await h.PublishAsync(rougher);
        await h.WaitForClassesAsync(measured[0], ScaleAt(TwoStepsUp));

        Assert.Equal(measured.Append(rougher).Select(session => session.Id).Order(), h.ClassPublications[^1].Order());
        Assert.All(measured, session => Assert.Equal(0, h.Sessions.Get(session.Id)!.TerrainClassSummary!.RoughShare));
    }

    [Fact]
    public async Task LabelInsideARideChangesTheClassesOfThatRideOnTheSameScale()
    {
        await using var h = new Harness();
        var measured = h.ReadySetup(Guid.NewGuid());
        await h.StartAsync(measured);
        await h.WaitForClassesAsync(measured[0], ScaleAt(0));
        await h.PublishSourceAsync(measured[0]);

        await h.PublishLabelAsync(measured[0], 0, 1, RoughnessClass.Smooth);
        await h.WaitForLabelledClassesAsync(measured[0]);

        Assert.Equal(ScaleAt(0), h.Sessions.Get(measured[0].Id)!.TerrainClassKey!.Scale);
        Assert.Equal(0.5, h.Sessions.Get(measured[0].Id)!.TerrainClassSummary!.RoughShare);
        Assert.Equal(1, h.Sessions.Get(measured[1].Id)!.TerrainClassSummary!.RoughShare);
    }

    [Fact]
    public async Task SourceThatArrivesAfterItsLabelGivesTheLabelledClasses()
    {
        await using var h = new Harness();
        var measured = h.ReadySetup(Guid.NewGuid());
        await h.StartAsync(measured);
        await h.WaitForClassesAsync(measured[0], ScaleAt(0));
        await h.PublishLabelAsync(measured[0], 0, 1, RoughnessClass.Smooth);

        await h.PublishSourceAsync(measured[0]);
        await h.WaitForLabelledClassesAsync(measured[0]);

        Assert.Equal(0.5, h.Sessions.Get(measured[0].Id)!.TerrainClassSummary!.RoughShare);
        Assert.NotNull(h.Sessions.Get(measured[0].Id)!.TerrainLabelSummary);
    }

    [Fact]
    public async Task ProfileWorkRunsBeforeLabelWorkAndLabelWorkBeforeClassWork()
    {
        await using var h = new Harness();
        var measured = h.ReadySetup(Guid.NewGuid());
        var order = new List<string>();
        h.Repository.BeforeWrite = () => Record("profile");
        h.Repository.BeforeLabelWrite = _ => Record("labels");
        h.Repository.BeforeClassWrite = () => Record("classes");
        await h.PublishSourceAsync(measured[0]);
        await h.PublishLabelAsync(measured[0], 0, 1, RoughnessClass.Smooth);

        await h.StartAsync([.. measured, Session()]);
        await h.WaitForLabelledClassesAsync(measured[0]);

        Assert.Equal(["profile", "labels", "classes"], order.Distinct());

        Task Record(string work)
        {
            lock (order) order.Add(work);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LabelRunThatFailsOrWritesNoRowPublishesNothingAndTheClassRunsStart(bool fails)
    {
        await using var h = new Harness();
        var measured = h.ReadySetup(Guid.NewGuid());
        var labelled = measured[0];
        if (fails) h.Repository.LabelWriteFailure = new IOException("storage unavailable");
        else h.Repository.BeforeLabelWrite = _ =>
        {
            h.Repository.Seed(labelled.Id, labelled.TerrainProfileKey! with { AlgorithmVersion = 0 }, Harness.RoughProfile);
            return Task.CompletedTask;
        };
        await h.PublishSourceAsync(labelled);
        await h.PublishLabelAsync(labelled, 0, 1, RoughnessClass.Smooth);

        await h.StartAsync(measured);
        await h.WaitForClassesAsync(measured[1], ScaleAt(0));

        Assert.Null(h.Sessions.Get(labelled.Id)!.TerrainLabelKey);
    }

    [Fact]
    public async Task LabelChangeAfterTheProfileReadCancelsTheLabelRunAndTheNextRunWritesTheNewKey()
    {
        await using var h = new Harness();
        var measured = h.ReadySetup(Guid.NewGuid());
        var writing = new TaskCompletionSource();
        h.Repository.BeforeLabelWrite = token =>
        {
            if (!writing.TrySetResult()) return Task.CompletedTask;
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        await h.PublishSourceAsync(measured[0]);
        await h.PublishLabelAsync(measured[0], 0, 1, RoughnessClass.Smooth);
        await h.StartAsync(measured);
        await h.DrainAsync(writing.Task);

        await h.PublishLabelAsync(measured[0], 1, 2, RoughnessClass.Smooth);
        await h.DrainAsync(h.Sessions.Watch(measured[0].Id).Where(value => value.TerrainLabelKey?.Labels.Ranges.Length == 2).Take(1)
            .ToTask(TestContext.Current.CancellationToken));

        Assert.All(h.Repository.LabelWrites, write => Assert.Equal(2, write[measured[0].Id].Key.Labels.Ranges.Length));
    }

    [Fact]
    public async Task SupersededClassRunWritesNothing()
    {
        await using var h = new Harness();
        var setup = Guid.NewGuid();
        var measured = h.ReadySetup(setup);
        var started = new TaskCompletionSource();
        var reads = 0;
        h.Repository.BeforeRead = async (_, token) =>
        {
            if (Interlocked.Increment(ref reads) > 1) return;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        await h.StartAsync(measured);
        await h.DrainAsync(started.Task);

        var rougher = h.Measured(setup, 104, bin: TwoStepsUp, metres: 10000);
        await h.PublishAsync(rougher);
        await h.WaitForClassesAsync(measured[0], ScaleAt(TwoStepsUp));

        Assert.All(h.PublishedClassKeys, key => Assert.Equal(ScaleAt(TwoStepsUp), key.Scale));
    }

    [Fact]
    public async Task ClassWriteSkipsASessionWhoseProfileChanged()
    {
        await using var h = new Harness();
        var setup = Guid.NewGuid();
        var measured = h.ReadySetup(setup);
        var changed = measured[1];
        h.Repository.BeforeClassWrite = () =>
        {
            h.Repository.Seed(changed.Id, changed.TerrainProfileKey! with { AlgorithmVersion = 0 }, Harness.RoughProfile);
            return Task.CompletedTask;
        };
        await h.StartAsync(measured);
        await h.WaitForClassesAsync(measured[0], ScaleAt(0));

        Assert.DoesNotContain(changed.Id, Assert.Single(h.ClassPublications));
        Assert.Null(h.Sessions.Get(changed.Id)!.TerrainClassKey);
    }

    // Twenty level bins span one class step.
    private const int TwoStepsUp = 40;

    private static TerrainScaleKey ScaleAt(int frameBin) => new(frameBin, null, null, null);

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
        public TerrainLabelStore Labels { get; }
        public RecordedSessionSourceStore Sources { get; }
        public InMemoryTerrainProfileRepository Repository { get; } = new();
        public IFullTrackPointReader Points { get; } = Substitute.For<IFullTrackPointReader>();
        public TerrainProfileCoordinator Coordinator { get; }
        public List<(Guid Id, TerrainProfileKey Key)> Published { get; } = [];
        public List<Guid> Reads { get; } = [];
        public List<IReadOnlyList<Guid>> ClassPublications { get; } = [];
        public List<TerrainClassKey> PublishedClassKeys { get; } = [];
        private readonly IDisposable classPublications;

        /// <summary>Two Rough windows on a scale whose frame median is in bin 0.</summary>
        public static TerrainProfile RoughProfile { get; } = TerrainProfiles.Profile(
            [TerrainProfiles.Window(0, RoughnessClass.Rough), TerrainProfiles.Window(1, RoughnessClass.Rough)]);
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
            classPublications = Sessions.Connect().Subscribe(changes =>
            {
                var classed = changes.Where(change => change.Current.TerrainClassKey is { } key &&
                                                      (!change.Previous.HasValue || change.Previous.Value.TerrainClassKey != key))
                    .ToArray();
                if (classed.Length == 0) return;
                ClassPublications.Add([.. classed.Select(change => change.Current.Id)]);
                PublishedClassKeys.AddRange(classed.Select(change => change.Current.TerrainClassKey!));
            });
            Labels = new TerrainLabelStore(publication);
            Sources = new RecordedSessionSourceStore(publication);
            Coordinator = new(Sessions, tracks, content, Points, Repository, runner, Dispatcher, publication, Labels,
                new TerrainLabelQuery(Sessions, Sources, Labels), Sources);
        }

        /// <summary>
        /// A session of the setup with a stored <see cref="RoughProfile"/> and frame readings of the given distance in
        /// one level bin.
        /// </summary>
        public SessionSnapshot Measured(Guid setupId, long timestamp, int bin = 0, double metres = 2000)
        {
            var distribution = new LevelDistribution(System.Collections.Immutable.ImmutableSortedDictionary<int, double>.Empty
                .Add(bin, metres));
            var session = TestSnapshots.WithCurrentTerrain(
                TestSnapshots.Session(setupId: setupId, timestamp: timestamp, hasProcessedData: true) with { DurationSeconds = 12 },
                TerrainOutcome.Computed, new TerrainSummary(metres, null, FrameImu: distribution), [track]);
            Repository.Seed(session.Id, session.TerrainProfileKey!, RoughProfile);
            return session;
        }

        /// <summary>Three sessions of the setup, newest first, whose readings make its frame scale ready at bin 0.</summary>
        public SessionSnapshot[] ReadySetup(Guid setupId) =>
            [Measured(setupId, 103), Measured(setupId, 102), Measured(setupId, 101)];

        public Task WaitForClassesAsync(SessionSnapshot session, TerrainScaleKey scale) => DrainAsync(Sessions.Watch(session.Id)
            .Where(value => value.TerrainClassKey?.Scale == scale).Take(1).ToTask(TestContext.Current.CancellationToken));

        public Task WaitForLabelledClassesAsync(SessionSnapshot session) => DrainAsync(Sessions.Watch(session.Id)
            .Where(value => value.TerrainClassKey?.Labels.Ranges.IsEmpty == false).Take(1).ToTask(TestContext.Current.CancellationToken));

        /// <summary>Publishes the recording source of the session; its hash is the session ID.</summary>
        public Task PublishSourceAsync(SessionSnapshot session) => DrainAsync(Sources.PublishSourcesChangedAsync(
            [new RecordedSessionSourceSnapshot(session.Id, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, session.Id.ToString())]));

        public Task PublishLabelAsync(SessionSnapshot session, double start, double end, RoughnessClass roughness) =>
            DrainAsync(Labels.PublishLabelsAsync([new TerrainLabelSnapshot(Guid.NewGuid(), session.Id.ToString(), start, end, roughness)], []));

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
            classPublications.Dispose();
            tracks.Dispose();
            Labels.Dispose();
            Sources.Dispose();
            Sessions.Dispose();
            publication.Dispose();
        }
    }
}
