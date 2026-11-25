using System.Collections.Immutable;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Queries;

public class SetupTerrainScaleQueryTests
{
    private static readonly Guid SetupId = Guid.NewGuid();

    [Theory]
    [InlineData(3, 1700, true)]
    [InlineData(2, 2600, false)]
    [InlineData(3, 1600, false)]
    public void SensorIsReadyFromThreeRidesAndFiveKilometres(int rides, double metresPerRide, bool ready)
    {
        var scale = Evaluate(Enumerable.Range(0, rides).Select(_ => Ride((5, metresPerRide))).ToArray());
        if (ready)
            Assert.Equal(new SensorScale.Ready(5), scale.FrameImu);
        else
            Assert.Equal(new SensorScale.NotEnoughData(rides, rides * metresPerRide), scale.FrameImu);
    }

    // Merged: bin 2 1000 m, bin 4 1500 m, bin 8 1000 m, bin 10 2000 m. Half of 5500 m is reached in bin 8.
    [Fact]
    public void MedianIsTheBinAtWhichTheMergedDistanceReachesHalf()
    {
        var scale = Evaluate(Ride((2, 1000), (8, 1000)), Ride((4, 1500)), Ride((10, 2000)));
        Assert.Equal(new SensorScale.Ready(8), scale.FrameImu);
    }

    [Fact]
    public void RideThatLeavesTheMedianInItsBinKeepsTheKey()
    {
        SessionSnapshot[] rides = [Ride((2, 1000), (8, 1000)), Ride((4, 1500)), Ride((10, 2000))];
        Assert.Equal(Evaluate(rides).Key, Evaluate([.. rides, Ride((8, 400))]).Key);
    }

    [Fact]
    public void SessionWithoutACurrentProfileDoesNotContribute()
    {
        var stale = Ride((5, 3000)) with { TerrainProfileKey = null };
        var scale = Evaluate(Ride((5, 1000)), Ride((5, 1000)), Ride((5, 1000)), stale);
        Assert.Equal(new SensorScale.NotEnoughData(3, 3000), scale.FrameImu);
    }

    [Fact]
    public void SessionWithoutASetupHasNoScale()
    {
        var session = Ride((5, 6000)) with { SetupId = null };
        Assert.Empty(SetupTerrainScaleQuery.Evaluate([session], []));
    }

    [Fact]
    public void CatalogueHoldsTheScaleKeyOfEachSetup()
    {
        var other = Guid.NewGuid();
        var key = new TerrainScaleKey(3, null, 5, null);
        var catalog = SetupTerrainScaleQuery.Catalog([Summary(SetupId, key), Summary(other, TerrainScaleKey.None),
            Summary(null, key), Summary(Guid.NewGuid(), null)]);
        Assert.Equal(new TerrainScaleCatalog(ImmutableDictionary<Guid, TerrainScaleKey>.Empty
            .Add(SetupId, key).Add(other, TerrainScaleKey.None)), catalog);
    }

    private static RecordedSessionSummary Summary(Guid? setupId, TerrainScaleKey? scale) =>
        new(Guid.NewGuid(), 0, "ride", "", null, true, new SessionStaleness.Current(), SetupId: setupId)
        {
            Terrain = new TerrainStatus.Pending(), TerrainScale = scale
        };

    private static SetupTerrainScale Evaluate(params SessionSnapshot[] sessions) =>
        SetupTerrainScaleQuery.Evaluate(sessions, [])[SetupId];

    private static SessionSnapshot Ride(params (int Bin, double Metres)[] frame)
    {
        var distribution = new LevelDistribution(frame.ToImmutableSortedDictionary(b => b.Bin, b => b.Metres));
        var summary = new TerrainSummary(frame.Sum(b => b.Metres), null, 0, 0, null, FrameImu: distribution);
        return TestSnapshots.WithCurrentTerrain(TestSnapshots.Session(setupId: SetupId, hasProcessedData: true),
            TerrainOutcome.Computed, summary);
    }
}
