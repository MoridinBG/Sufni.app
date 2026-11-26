using System.Collections.Immutable;

namespace Sufni.Telemetry.Tests;

public class TerrainLabelSummaryTests
{
    private const double Level = 2;

    [Theory]
    [InlineData(RoughnessClass.Smooth, 2)]
    [InlineData(RoughnessClass.Medium, 0)]
    [InlineData(RoughnessClass.Rough, -1)]
    public void LabelledReadingLandsInItsBinMovedByTheStepsOfItsClassFromMedium(RoughnessClass label, int steps)
    {
        var reading = TerrainProfiles.Sensor(Level);
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = reading, Fork = reading, RearAxle = reading }]);

        var summary = TerrainLabels.Summarize(profile, Label(label));

        var expected = Bins(TerrainLevels.Bin(Level) + steps * TerrainLevels.BinsPerStep);
        Assert.Equal(new TerrainLabelSummary(expected, expected, expected, null, null), summary);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Level, 0.05)]
    public void ReadingThatIsNotEligibleAddsNothing(double level, double clippedShare)
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = TerrainProfiles.Sensor(level, clippedShare) }]);

        Assert.Equal(new TerrainLabelSummary(null, null, null, null, null), TerrainLabels.Summarize(profile, Label(RoughnessClass.Rough)));
    }

    [Fact]
    public void ClimbLabelsFillTheirClimbDistributionWithTheRawFrameLevel()
    {
        var climb = TerrainProfiles.Window(0) with { Band = GradeBand.Climb, Frame = new SensorRoughness(Level, 1, 1, 1, 9, 0) };
        var profile = TerrainProfiles.Profile([climb, climb with { StartSeconds = 1, EndSeconds = 2 }]);
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.SmoothClimb),
            new TerrainLabelRange(1, 2, RoughnessClass.TechnicalClimb));

        var summary = TerrainLabels.Summarize(profile, labels);

        Assert.Equal(new TerrainLabelSummary(null, null, null, Bins(TerrainLevels.Bin(Level)), Bins(TerrainLevels.Bin(Level))), summary);
    }

    [Fact]
    public void WindowWithoutLabelAddsNothing()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = TerrainProfiles.Sensor(Level) }]);

        Assert.Equal(new TerrainLabelSummary(null, null, null, null, null),
            TerrainLabels.Summarize(profile, TerrainProfiles.Labels(new TerrainLabelRange(5, 9, RoughnessClass.Rough))));
    }

    private static TerrainLabelSet Label(RoughnessClass roughness) => TerrainProfiles.Labels(new TerrainLabelRange(0, 1, roughness));

    private static LevelDistribution Bins(int bin) =>
        new(ImmutableSortedDictionary<int, double>.Empty.Add(bin, TerrainProfileCalculator.WindowMetres));
}
