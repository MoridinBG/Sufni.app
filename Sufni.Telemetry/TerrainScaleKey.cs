namespace Sufni.Telemetry;

/// <summary>
/// The ready median level bins of a setup's scale, per sensor: the classifier input and the scale fingerprint.
/// A null bin is a sensor without enough rides or distance.
/// </summary>
public sealed record TerrainScaleKey(int? FrameImu, int? ForkImu, int? RearAxleImu, int? FrameClimb)
{
    public static TerrainScaleKey None { get; } = new(null, null, null, null);
}
