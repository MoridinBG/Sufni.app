using System.Collections.Immutable;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

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

    [Theory]
    [InlineData(100, true)]
    [InlineData(90, false)]
    public void LabelledReadingsAnchorASensorFromOneHundredMetres(double metres, bool anchored)
    {
        var rides = Enumerable.Range(0, 3).Select(_ => Ride((5, 2000))).ToArray();
        rides[0] = Labelled(rides[0], Stretches(TerrainProfiles.Stretch(RoughnessClass.Medium, TerrainLevels.Median(9), metres: metres)));

        var scale = Evaluate(rides);

        Assert.Equal(anchored ? new SensorScale.Anchored(9) : new SensorScale.Ready(5), scale.FrameImu);
        Assert.Equal(new TerrainLabelCoverage(metres, 0, 0, 0, 0), scale.Labels);
    }

    [Fact]
    public void LabelsOfSeveralClassesAnchorTheMedianTheyAgreeOn()
    {
        var median = 40;
        var profile = TerrainProfiles.Profile(Enumerable.Range(0, 20).Select(i => TerrainProfiles.Window(i) with
        {
            Frame = TerrainProfiles.Sensor(TerrainLevels.Median(median + (i < 10 ? 1 : -1) * TerrainLevels.BinsPerStep))
        }));
        var labels = new TerrainLabelSet([new TerrainLabelRange(0, 10, RoughnessClass.Rough), new TerrainLabelRange(10, 20, RoughnessClass.Calm)]);

        var scale = Evaluate(Labelled(Ride((5, 2000)), TerrainLabels.Summarize(profile, labels)));

        Assert.Equal(new SensorScale.Anchored(median), scale.FrameImu);
    }

    [Fact]
    public void EachLabelCountsOnceWhateverItsLength()
    {
        var shortLabels = Enumerable.Range(0, 3).Select(i => TerrainProfiles.Stretch(RoughnessClass.Medium, TerrainLevels.Median(10), start: i));
        var longLabel = TerrainProfiles.Stretch(RoughnessClass.Medium, TerrainLevels.Median(50), start: 3, metres: 1000);

        var scale = Evaluate(Labelled(Ride((5, 2000)), Stretches([.. shortLabels, longLabel])));

        Assert.Equal(new SensorScale.Anchored(10), scale.FrameImu);
    }

    [Fact]
    public void NewRideDoesNotMoveAnAnchoredSensor()
    {
        SessionSnapshot[] rides = [Labelled(Ride((5, 2000)), Stretches(TerrainProfiles.Stretch(RoughnessClass.Medium, TerrainLevels.Median(9), metres: 200))),
            Ride((5, 2000)), Ride((5, 2000))];

        Assert.Equal(Evaluate(rides).FrameImu, Evaluate([.. rides, Ride((30, 9000))]).FrameImu);
    }

    [Fact]
    public void SessionInThePendingLabelStateAddsNoLabelledReading()
    {
        var rides = Enumerable.Range(0, 3).Select(_ => Ride((5, 2000))).ToArray();
        rides[0] = Labelled(rides[0], Stretches(TerrainProfiles.Stretch(RoughnessClass.Medium, TerrainLevels.Median(9), metres: 200)))
            with { TerrainLabelKey = null };

        var scale = Evaluate(rides);

        Assert.Equal(new SensorScale.Ready(5), scale.FrameImu);
        Assert.Equal(TerrainLabelCoverage.None, scale.Labels);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(90, false)]
    public void ClimbLimitNeedsOneHundredMetresOfEachClimbClassAndSitsBetweenTheirMedians(double technicalMetres, bool anchored)
    {
        var ride = Labelled(Ride((5, 2000)), Stretches(TerrainProfiles.Stretch(RoughnessClass.SmoothClimb, TerrainLevels.Median(2)),
            TerrainProfiles.Stretch(RoughnessClass.TechnicalClimb, TerrainLevels.Median(7), start: 1, metres: technicalMetres)));

        var scale = Evaluate(ride);

        Assert.Equal(anchored ? new SensorScale.Anchored(4) : new SensorScale.NotEnoughData(0, 0), scale.FrameClimb);
        Assert.Equal(new TerrainLabelCoverage(0, 0, 0, 100, technicalMetres), scale.Labels);
    }

    [Fact]
    public void SessionWithoutASetupHasNoScale()
    {
        var session = Ride((5, 6000)) with { SetupId = null };
        Assert.Empty(SetupTerrainScaleQuery.Evaluate([session], [], new Dictionary<Guid, TerrainLabelSet>()));
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
        SetupTerrainScaleQuery.Evaluate(sessions, [], sessions.ToDictionary(session => session.Id, _ => Labels))[SetupId];

    private static readonly TerrainLabelSet Labels = new([new TerrainLabelRange(0, 100, RoughnessClass.Medium)]);

    /// <summary>The ride with a current label summary of <see cref="Labels"/>.</summary>
    private static SessionSnapshot Labelled(SessionSnapshot ride, TerrainLabelSummary summary) => ride with
    {
        TerrainLabelKey = new(ride.TerrainProfileKey!, Labels, TerrainLabels.Version), TerrainLabelSummary = summary
    };

    private static TerrainLabelSummary Stretches(params LabelledStretch[] stretches) => new([.. stretches]);

    private static SessionSnapshot Ride(params (int Bin, double Metres)[] frame)
    {
        var distribution = new LevelDistribution(frame.ToImmutableSortedDictionary(b => b.Bin, b => b.Metres));
        var summary = new TerrainSummary(frame.Sum(b => b.Metres), null, FrameImu: distribution);
        return TestSnapshots.WithCurrentTerrain(TestSnapshots.Session(setupId: SetupId, hasProcessedData: true),
            TerrainOutcome.Computed, summary);
    }
}
