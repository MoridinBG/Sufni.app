namespace Sufni.Telemetry.Tests;

public class TerrainRunAlignmentTests
{
    private static readonly Lazy<TerrainProfile> source = new(() => SyntheticTrailRuns.Record(11));
    private static readonly Lazy<TerrainProfile> shifted = new(() => SyntheticTrailRuns.Record(11, leadInMetres: 15));

    [Fact]
    public void GroundSignalRecoversFifteenMetresOfGpsBias()
    {
        var aligned = Align(shifted.Value);
        Assert.All(aligned.Chunks, chunk => Assert.InRange(chunk.ShiftMetres, 13, 17));
        Assert.True(aligned.Pairs.Length > 100);
    }

    [Fact]
    public void ChangingReboundOnRoughSectionsPreservesAlignedGround()
    {
        var aligned = Align(shifted.Value);
        var changed = Align(SyntheticTrailRuns.Record(11, leadInMetres: 15, roughRebound: 0.7));
        Assert.Equal(aligned.Chunks.ToArray(), changed.Chunks.ToArray());
        Assert.Equal(aligned.Pairs.ToArray(), changed.Pairs.ToArray());
    }

    [Fact]
    public void UnrelatedGroundIsUnaligned()
    {
        var other = SyntheticTrailRuns.Record(91, variedGround: false);
        Assert.IsType<TerrainRunAlignment.Unaligned>(TerrainRunAlignment.Calculate(source.Value, other,
            SyntheticTrailRuns.Map(source.Value, other)));
    }

    [Fact]
    public void DisjointSensorLocationsCannotAlign()
    {
        var frame = SyntheticTrailRuns.Record(11, lowers: false);
        var lowers = SyntheticTrailRuns.Record(11, frame: false);
        Assert.IsType<TerrainRunAlignment.NoCommonSensor>(TerrainRunAlignment.Calculate(frame, lowers,
            SyntheticTrailRuns.Map(frame, lowers)));
    }

    [Fact]
    public void FrameMissingFromOneRunUsesLowers()
    {
        var aligned = Align(SyntheticTrailRuns.Record(11, frame: false));
        Assert.Equal(ImuLocation.Fork, aligned.Sensor);
        Assert.All(aligned.Chunks, chunk => Assert.InRange(chunk.ShiftMetres, -2, 2));
    }

    private static TerrainRunAlignment.Aligned Align(TerrainProfile other) =>
        Assert.IsType<TerrainRunAlignment.Aligned>(TerrainRunAlignment.Calculate(source.Value, other,
            SyntheticTrailRuns.Map(source.Value, other)));
}
