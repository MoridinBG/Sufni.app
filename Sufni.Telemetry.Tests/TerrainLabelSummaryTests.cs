namespace Sufni.Telemetry.Tests;

public class TerrainLabelSummaryTests
{
    private static readonly TerrainLabelRange Rough = new(0, 10, RoughnessClass.Rough);

    [Theory]
    [InlineData(new[] { 1d, 9, 2 }, 2)]
    [InlineData(new[] { 4d, 1 }, 2)]
    public void StretchLevelIsTheMedianOfItsReadings(double[] levels, double expected)
    {
        var profile = TerrainProfiles.Profile(levels.Select((level, i) => TerrainProfiles.Window(i) with { Frame = TerrainProfiles.Sensor(level) }));

        var stretch = Assert.Single(TerrainLabels.Summarize(profile, TerrainProfiles.Labels(Rough)).Stretches);

        Assert.Equal(expected, stretch.FrameImu!.Level, 9);
        Assert.Equal(levels.Length * TerrainProfileCalculator.WindowMetres, stretch.FrameImu.Metres);
    }

    [Theory]
    [InlineData(RoughnessClass.Rough, null, 9d)]
    [InlineData(RoughnessClass.TechnicalClimb, GradeBand.Climb, 2d)]
    public void DescentLabelReadsTheSpeedRemovedLevelAndClimbLabelTheRawFrameLevel(RoughnessClass label, GradeBand? band, double expected)
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Band = band, Frame = new SensorRoughness(2, 1, 1, 1, 9, 0) }]);

        var summary = TerrainLabels.Summarize(profile, TerrainProfiles.Labels(new TerrainLabelRange(0, 1, label)));

        Assert.Equal(expected, Assert.Single(summary.Stretches).FrameImu!.Level);
    }

    [Fact]
    public void EachLabelGivesItsOwnStretchInLabelOrder()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = TerrainProfiles.Sensor(1) },
            TerrainProfiles.Window(1) with { Frame = TerrainProfiles.Sensor(3) }]);
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Calm), new TerrainLabelRange(1, 2, RoughnessClass.Rough));

        var summary = TerrainLabels.Summarize(profile, labels);

        Assert.Equal([(labels.Ranges[0], 1d), (labels.Ranges[1], 3d)], summary.Stretches.Select(s => (s.Label, s.FrameImu!.Level)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0.05)]
    public void ReadingThatIsNotEligibleGivesNoStretch(double level, double clippedShare)
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = TerrainProfiles.Sensor(level, clippedShare) }]);

        Assert.Empty(TerrainLabels.Summarize(profile, TerrainProfiles.Labels(Rough)).Stretches);
    }

    [Fact]
    public void WindowWithoutLabelGivesNoStretch()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = TerrainProfiles.Sensor(2) }]);

        Assert.Empty(TerrainLabels.Summarize(profile, TerrainProfiles.Labels(new TerrainLabelRange(5, 9, RoughnessClass.Rough))).Stretches);
    }
}
