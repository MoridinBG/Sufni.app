using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

/// <summary>The median level of one sensor over the rides of a setup, once it has enough of them.</summary>
public closed record SensorScale
{
    private SensorScale() { }
    public sealed record Ready(int MedianBin) : SensorScale;
    public sealed record NotEnoughData(int Rides, double Metres) : SensorScale;
}

/// <summary>The terrain scale of one setup, derived from the measurement summaries of its current profiles.</summary>
public sealed record SetupTerrainScale(SensorScale FrameImu, SensorScale ForkImu, SensorScale RearAxleImu, SensorScale FrameClimb)
{
    public TerrainScaleKey Key => new(Bin(FrameImu), Bin(ForkImu), Bin(RearAxleImu), Bin(FrameClimb));

    private static int? Bin(SensorScale scale) => (scale as SensorScale.Ready)?.MedianBin;
}
