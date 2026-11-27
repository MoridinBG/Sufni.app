using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Formatting;

public class TerrainClassTextTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(RoughnessClass.Rough, null)]
    [InlineData(RoughnessClass.Calm, "Reads as Calm")]
    public void ReadsAsNamesOnlyAReadingThatDiffersFromTheLabel(RoughnessClass? reads, string? expected) =>
        Assert.Equal(expected, TerrainClassText.ReadsAs(RoughnessClass.Rough,
            reads is { } roughness ? new LabelReading(roughness, 0) : null));
}
