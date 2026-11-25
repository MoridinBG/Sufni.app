namespace Sufni.Telemetry.Tests;

public class TerrainClassifierTests
{
    private const double Clipped = 0.05;

    [Theory]
    [InlineData(-2, RoughnessClass.Smooth)]
    [InlineData(-1, RoughnessClass.Calm)]
    [InlineData(0, RoughnessClass.Medium)]
    [InlineData(1, RoughnessClass.Rough)]
    [InlineData(2, RoughnessClass.VeryRough)]
    public void ReadingAtTheMedianIsMediumAndEachStepMovesOneLevel(int steps, RoughnessClass expected)
    {
        var window = Single(Descent() with { Frame = Reading(steps) });
        Assert.Equal(expected, window.Roughness);
        Assert.Equal(GroundSensors.Frame, window.Source);
    }

    [Fact]
    public void UnsprungReadingsGiveTheMeanOfTheirValuesAndLeaveOutTheFrame()
    {
        var window = Single(Descent() with { Fork = Reading(0), RearAxle = Reading(2), Frame = Reading(-2) });
        Assert.Equal((RoughnessClass.Rough, GroundSensors.ForkAndRearAxle), (window.Roughness, window.Source));
        Assert.Equal(1.5, window.ClassValue!.Value, 9);
    }

    [Fact]
    public void FrameReadsTheGroundOnlyWithoutAnUnclippedUnsprungReading()
    {
        var window = Single(Descent() with { Fork = Reading(2, Clipped), RearAxle = Reading(2, Clipped), Frame = Reading(0) });
        Assert.Equal((RoughnessClass.Medium, GroundSensors.Frame), (window.Roughness, window.Source));
    }

    [Fact]
    public void UnclippedUnsprungReadingLeavesOutTheClippedOne()
    {
        var window = Single(Descent() with { Fork = Reading(2, Clipped), RearAxle = Reading(0), Frame = Reading(-2) });
        Assert.Equal((RoughnessClass.Medium, GroundSensors.RearAxle), (window.Roughness, window.Source));
    }

    [Fact]
    public void ClippedUnsprungReadingsReadTheGroundWithoutAFrameReading()
    {
        var window = Single(Descent() with { Fork = Reading(1, Clipped) });
        Assert.Equal((RoughnessClass.Rough, GroundSensors.Fork), (window.Roughness, window.Source));
    }

    [Fact]
    public void ZeroLevelReadingSetsNoClass()
    {
        var window = Single(Descent() with { Frame = TerrainProfiles.Sensor(0) });
        Assert.Equal(new ClassifiedWindow(window.Window, null, null, null), window);
    }

    [Fact]
    public void SensorWithoutAReadyMedianSetsNoClass()
    {
        var profile = TerrainProfiles.Profile([Descent() with { Fork = Reading(2), Frame = Reading(0) }]);
        var window = TerrainClassifier.Classify(profile, TerrainProfiles.Scale with { ForkImu = null }).Windows.Single();
        Assert.Equal((RoughnessClass.Medium, GroundSensors.Frame), (window.Roughness, window.Source));
    }

    [Theory]
    [InlineData(0.99, RoughnessClass.SmoothClimb)]
    [InlineData(1, RoughnessClass.TechnicalClimb)]
    public void ClimbBelowTheClimbMedianIsSmoothAndAtItTechnical(double ofMedian, RoughnessClass expected)
    {
        var window = Single(TerrainProfiles.Window(0) with
        {
            Band = GradeBand.Climb, Frame = TerrainProfiles.Sensor(ofMedian * TerrainLevels.Median(0))
        });
        Assert.Equal(new ClassifiedWindow(window.Window, expected, null, null), window);
    }

    [Theory]
    [InlineData(0, RoughnessClass.Calm)]
    [InlineData(0.5, RoughnessClass.Rough)]
    public void SingleWindowBetweenTwoOfAnotherClassTakesTheirClassOnlyWithoutAGap(double gapSeconds, RoughnessClass expected)
    {
        var windows = Enumerable.Range(0, 3).Select(i => TerrainProfiles.Window(i + (i == 2 ? gapSeconds : 0)) with
        {
            Frame = Reading(i == 1 ? 1 : -1)
        }).ToArray();
        var middle = TerrainProfiles.Classified(windows).Windows[1];
        Assert.Equal(expected, middle.Roughness);
        Assert.Equal(1.5, middle.ClassValue!.Value, 9);
    }

    [Fact]
    public void ScaleWithoutAReadyBinSetsNoClass()
    {
        var profile = TerrainProfiles.Profile([
            Descent() with { Fork = Reading(0), RearAxle = Reading(0), Frame = Reading(0) },
            TerrainProfiles.Window(1) with { Band = GradeBand.Climb, Frame = Reading(0) }
        ]);
        Assert.All(TerrainClassifier.Classify(profile, TerrainScaleKey.None).Windows, window =>
            Assert.Equal(new ClassifiedWindow(window.Window, null, null, null), window));
    }

    [Theory]
    [InlineData(0, RoughnessClass.Calm)]
    [InlineData(1, RoughnessClass.Medium)]
    [InlineData(2, RoughnessClass.Rough)]
    [InlineData(3, RoughnessClass.VeryRough)]
    public void EachEdgeStartsTheNextLevel(int edge, RoughnessClass above)
    {
        var level = TerrainLevels.Edges(0)[edge];
        Assert.Equal(above, Single(Descent() with { Frame = TerrainProfiles.Sensor(level * 1.0001) }).Roughness);
        Assert.Equal(above - 1, Single(Descent() with { Frame = TerrainProfiles.Sensor(level * 0.9999) }).Roughness);
    }

    private static TerrainWindow Descent() => TerrainProfiles.Window(0) with { Band = GradeBand.Descent };

    /// <summary>A reading the given number of class steps from the median of <see cref="TerrainProfiles.Scale"/>.</summary>
    private static SensorRoughness Reading(double steps, double clippedShare = 0) =>
        TerrainProfiles.Sensor(TerrainLevels.Median(0) * Math.Pow(TerrainLevels.Step, steps), clippedShare: clippedShare);

    private static ClassifiedWindow Single(TerrainWindow window) => TerrainProfiles.Classified(window).Windows.Single();
}
