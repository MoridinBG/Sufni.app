namespace Sufni.Telemetry.Tests;

public class TerrainLabelSetTests
{
    [Fact]
    public void SetsWithEqualRangesAreEqual()
    {
        var a = TerrainProfiles.Labels(new TerrainLabelRange(0, 5, RoughnessClass.Rough), new(5, 9, RoughnessClass.Calm));
        var b = TerrainProfiles.Labels(new TerrainLabelRange(0, 5, RoughnessClass.Rough), new(5, 9, RoughnessClass.Calm));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [InlineData(0.5, 2, true)]
    [InlineData(0, 0.5, false)]
    public void RangeLabelsAWindowWhenItHoldsTheMidpointOfTheWindow(double start, double end, bool labelled)
    {
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(start, end, RoughnessClass.Rough));

        Assert.Equal(labelled ? RoughnessClass.Rough : null, labels.ClassAt(TerrainProfiles.Window(0)));
    }

    [Fact]
    public void WindowThatIsNotMovingHasNoLabel()
    {
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Rough));

        Assert.Null(labels.ClassAt(TerrainProfiles.Window(0, moving: false)));
    }

    [Theory]
    [InlineData(RoughnessClass.Rough, GradeBand.Descent, true)]
    [InlineData(RoughnessClass.Rough, GradeBand.Climb, false)]
    [InlineData(RoughnessClass.TechnicalClimb, GradeBand.Climb, true)]
    [InlineData(RoughnessClass.TechnicalClimb, GradeBand.Flat, false)]
    public void ClassLabelsOnlyAWindowOfItsKind(RoughnessClass roughness, GradeBand band, bool labelled)
    {
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, roughness));

        Assert.Equal(labelled ? roughness : null, labels.ClassAt(TerrainProfiles.Window(0) with { Band = band }));
    }

    [Fact]
    public void FirstOfTwoOverlappingRangesCounts()
    {
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Smooth), new(0, 1, RoughnessClass.Rough));

        Assert.Equal(RoughnessClass.Smooth, labels.ClassAt(TerrainProfiles.Window(0)));
    }
}
