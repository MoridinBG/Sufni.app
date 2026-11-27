namespace Sufni.Telemetry.Tests;

public class TerrainLabelAnchorTests
{
    [Theory]
    [InlineData(RoughnessClass.Smooth)]
    [InlineData(RoughnessClass.Medium)]
    [InlineData(RoughnessClass.VeryRough)]
    public void SingleStretchReadsTheCentreOfItsClass(RoughnessClass label)
    {
        var level = TerrainProfiles.Steps(1.3);

        var value = TerrainLevels.Value(level, TerrainLabels.Anchor([(level, label)]));

        Assert.Equal((int)label - 1.5, value, 1);
    }

    [Fact]
    public void LabelsThatFitOneScaleAllReadTheirClass()
    {
        (double, RoughnessClass)[] stretches = [(TerrainProfiles.Steps(0.8), RoughnessClass.Rough), (TerrainProfiles.Steps(0), RoughnessClass.Rough)];

        var anchor = TerrainLabels.Anchor(stretches);

        Assert.All(stretches, s => Assert.Equal(RoughnessClass.Rough, TerrainLevels.Classify(TerrainLevels.Value(s.Item1, anchor))));
    }

    [Fact]
    public void MoreLabelsOutweighAClashingOne()
    {
        var agreeing = TerrainProfiles.Steps(0);

        var anchor = TerrainLabels.Anchor([(agreeing, RoughnessClass.Rough), (agreeing, RoughnessClass.Rough),
            (TerrainProfiles.Steps(3), RoughnessClass.Rough)]);

        Assert.Equal(RoughnessClass.Rough, TerrainLevels.Classify(TerrainLevels.Value(agreeing, anchor)));
    }

    [Fact]
    public void ClimbLimitPutsMostClimbLabelsOnTheirSide()
    {
        double[] smooth = [TerrainProfiles.Steps(0), TerrainProfiles.Steps(0.2), TerrainProfiles.Steps(3)];
        double[] technical = [TerrainProfiles.Steps(2), TerrainProfiles.Steps(2.2)];

        var limit = TerrainLevels.Median(TerrainLabels.ClimbAnchor(smooth, technical));

        Assert.Equal([true, true, false], smooth.Select(level => level < limit));
        Assert.All(technical, level => Assert.True(level >= limit));
    }
}
