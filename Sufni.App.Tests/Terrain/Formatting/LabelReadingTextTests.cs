using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Formatting;

public class LabelReadingTextTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(RoughnessClass.Rough, false)]
    [InlineData(RoughnessClass.Calm, true)]
    public void RoughnessTextNamesOnlyAReadingThatDiffersFromTheLabel(RoughnessClass? reads, bool named) =>
        Assert.Equal(named, LabelReadingText.Of(RoughnessClass.Rough,
            reads is { } roughness ? new LabelReading(roughness, 0) : null).Text is not null);

    [Theory]
    [InlineData(null, false)]
    [InlineData(GradeBand.SteepDescent, false)]
    [InlineData(GradeBand.Descent, true)]
    public void SteepnessTextNamesOnlyAReadingThatDiffersFromTheLabel(GradeBand? reads, bool named) =>
        Assert.Equal(named, LabelReadingText.Of(GradeBand.SteepDescent,
            reads is { } band ? new GradeReading(band, 9, 1) : null).Text is not null);
}
