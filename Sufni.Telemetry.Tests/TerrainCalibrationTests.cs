namespace Sufni.Telemetry.Tests;

public class TerrainCalibrationTests
{
    [Theory]
    [InlineData(ImuLocation.Frame)]
    [InlineData(ImuLocation.Fork)]
    public void EachLevelEdgeSeparatesTwoAdjacentDescentLevels(ImuLocation location)
    {
        var calibration = TerrainCalibration.For(location, 4096)!;
        var edges = calibration.LevelEdges;
        for (var i = 0; i < edges.Count; i++)
        {
            Assert.Equal((RoughnessClass)i, TerrainCalibration.Classify(calibration.Normalise(edges[i] * 0.99)));
            Assert.Equal((RoughnessClass)(i + 1), TerrainCalibration.Classify(calibration.Normalise(edges[i] * 1.01)));
        }
    }
}
