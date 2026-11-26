using NSubstitute;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.Stores;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Queries;

public sealed class TerrainLabelQueryTests : IDisposable
{
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly TerrainLabelStore labels = new(new StorePublication(new InlineUiThreadDispatcher()));
    private readonly Guid sourceId = Guid.NewGuid();
    private readonly TerrainLabelQuery query;

    public TerrainLabelQueryTests()
    {
        sourceStore.Get(sourceId).Returns(new RecordedSessionSourceSnapshot(
            sourceId, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, "source-hash"));
        query = new TerrainLabelQuery(sessionStore, sourceStore, labels);
    }

    public void Dispose() => labels.Dispose();

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

    private SessionSnapshot Session(double windowStart, double duration) => TestSnapshots.Session() with
    {
        SourceSessionId = sourceId, WindowStartSeconds = windowStart, DurationSeconds = duration
    };

    private static TerrainLabelSnapshot Label(double start, double end, RoughnessClass roughness) =>
        new(Guid.NewGuid(), "source-hash", start, end, roughness);
}
