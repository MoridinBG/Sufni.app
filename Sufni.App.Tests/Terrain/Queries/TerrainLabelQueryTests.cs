using NSubstitute;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.Stores;
using Sufni.Telemetry;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Queries;

public sealed class TerrainLabelQueryTests : IDisposable
{
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly TerrainLabelStore labels;
    private readonly Guid sourceId = Guid.NewGuid();
    private readonly TerrainLabelQuery query;

    public TerrainLabelQueryTests()
    {
        sourceStore.Get(sourceId).Returns(new RecordedSessionSourceSnapshot(
            sourceId, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, "source-hash"));
        labels = new(publication);
        var tracks = Substitute.For<ITrackStore>();
        tracks.GetAll().Returns([]);
        query = new TerrainLabelQuery(sessionStore, sourceStore, labels, tracks, publication);
    }

    public void Dispose()
    {
        labels.Dispose();
        publication.Dispose();
    }

    [Fact]
    public async Task LabelInsideTheSessionWindowIsInTheSetInTelemetrySeconds()
    {
        await labels.PublishLabelsAsync([Label(15, 20, RoughnessClass.Rough)], []);

        Assert.Equal([new TerrainLabelRange(5, 10, RoughnessClass.Rough)], query.LabelsOf(Session(10, 30)).Ranges);
    }

    [Theory]
    [InlineData(5, 15)]
    [InlineData(35, 45)]
    public async Task LabelThatTheWindowDoesNotHoldFullyIsNotInTheSet(double start, double end)
    {
        await labels.PublishLabelsAsync([Label(start, end, RoughnessClass.Rough)], []);

        Assert.Equal(TerrainLabelSet.None, query.LabelsOf(Session(10, 30)));
    }

    [Fact]
    public async Task SessionsOfOneSourceEachGetTheLabelsInTheirOwnWindow()
    {
        await labels.PublishLabelsAsync([Label(5, 8, RoughnessClass.Calm), Label(25, 28, RoughnessClass.Rough)], []);
        var first = Session(0, 20);
        var second = Session(20, 20);

        var sets = query.LabelsOf([first, second]);

        Assert.Equal([new TerrainLabelRange(5, 8, RoughnessClass.Calm)], sets[first.Id].Ranges);
        Assert.Equal([new TerrainLabelRange(5, 8, RoughnessClass.Rough)], sets[second.Id].Ranges);
    }

    [Fact]
    public async Task SetIsInOrderOfStart()
    {
        await labels.PublishLabelsAsync([Label(10, 12, RoughnessClass.Rough), Label(2, 4, RoughnessClass.Calm)], []);

        Assert.Equal([2d, 10d], query.LabelsOf(Session(0, 20)).Ranges.Select(r => r.StartSeconds));
    }

    [Fact]
    public async Task SessionWithoutSourceHasNoLabels()
    {
        await labels.PublishLabelsAsync([Label(2, 4, RoughnessClass.Calm)], []);

        Assert.Equal(TerrainLabelSet.None, query.LabelsOf(TestSnapshots.Session() with { DurationSeconds = 20 }));
    }

    [Fact]
    public void SessionsOfGivesTheSessionsOfTheSource()
    {
        var first = Session(0, 20);
        var second = Session(20, 20);
        sessionStore.GetAll().Returns([first, TestSnapshots.Session(), second]);

        Assert.Equal([first.Id, second.Id], query.SessionsOf("source-hash"));
    }

    [Fact]
    public async Task ForSetupGivesTheRowsOfItsSessionsNewestFirstAndTheScale()
    {
        var setupId = Guid.NewGuid();
        var older = Session(0, 20) with { SetupId = setupId, Timestamp = 100 };
        var newer = Session(20, 20) with { SetupId = setupId, Timestamp = 200 };
        sessionStore.GetAll().Returns([older, Session(0, 40), newer]);
        TerrainLabelSnapshot first = Label(2, 4, RoughnessClass.Calm), second = Label(22, 24, RoughnessClass.Rough),
            third = Label(30, 32, RoughnessClass.Smooth);
        await labels.PublishLabelsAsync([third, first, second], []);

        var result = query.ForSetup(setupId);

        Assert.Equal([(second.Id, newer.Id, 2d), (third.Id, newer.Id, 10d), (first.Id, older.Id, 2d)],
            result.Labels.Select(row => (row.LabelId, row.SessionId, row.Range.StartSeconds)));
        Assert.NotNull(result.Scale);
    }

    [Fact]
    public async Task ForSetupRowsReadTheirStretchOnTheSetupScale()
    {
        var setupId = Guid.NewGuid();
        await labels.PublishLabelsAsync([Label(2, 4, RoughnessClass.Rough), Label(6, 8, RoughnessClass.Rough), Label(10, 12, RoughnessClass.Rough)], []);
        var session = Labelled(Measured(setupId), steps: [1, 1, 4]);
        sessionStore.GetAll().Returns([session]);

        var result = query.ForSetup(setupId);

        Assert.Equal([false, false, true], result.Labels.Select(row => row.Reading!.Clashes));
    }

    [Fact]
    public async Task ForSetupRowHasNoReadingWithoutACurrentLabelSummary()
    {
        var setupId = Guid.NewGuid();
        await labels.PublishLabelsAsync([Label(2, 4, RoughnessClass.Rough)], []);
        var session = Labelled(Measured(setupId), steps: [1]) with { TerrainLabelKey = null };
        sessionStore.GetAll().Returns([session]);

        Assert.Null(Assert.Single(query.ForSetup(setupId).Labels).Reading);
    }

    [Fact]
    public void SetupWithoutASessionHasNoScaleAndNoRows()
    {
        sessionStore.GetAll().Returns([Session(0, 20)]);

        var result = query.ForSetup(Guid.NewGuid());

        Assert.Null(result.Scale);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task ChangesEmitsOnALabelPublication()
    {
        var changes = 0;
        using var subscription = query.Changes.Subscribe(_ => changes++);

        await labels.PublishLabelsAsync([Label(2, 4, RoughnessClass.Calm)], []);

        Assert.Equal(1, changes);
    }

    private SessionSnapshot Session(double windowStart, double duration) => TestSnapshots.Session() with
    {
        SourceSessionId = sourceId, WindowStartSeconds = windowStart, DurationSeconds = duration
    };

    private SessionSnapshot Measured(Guid setupId) => TestSnapshots.WithCurrentTerrain(
        TestSnapshots.Session(setupId: setupId, hasProcessedData: true) with
        {
            SourceSessionId = sourceId, WindowStartSeconds = 0, DurationSeconds = 40
        }, TerrainOutcome.Computed, new TerrainSummary(400, null));

    /// <summary>The session with a current label summary whose stretches read the given class steps from bin 0.</summary>
    private SessionSnapshot Labelled(SessionSnapshot session, double[] steps)
    {
        var set = query.LabelsOf(session);
        return session with
        {
            TerrainLabelKey = new(session.TerrainProfileKey!, set, TerrainLabels.Version),
            TerrainLabelSummary = new([.. set.Ranges.Select((range, i) =>
                TerrainProfiles.Stretch(range.Class, TerrainProfiles.Steps(steps[i])) with { Label = range })])
        };
    }

    private static TerrainLabelSnapshot Label(double start, double end, RoughnessClass roughness) =>
        new(Guid.NewGuid(), "source-hash", start, end, roughness);
}
