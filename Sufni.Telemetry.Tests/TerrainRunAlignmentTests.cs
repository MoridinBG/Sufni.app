namespace Sufni.Telemetry.Tests;

public class TerrainRunAlignmentTests
{
    private static readonly Lazy<TerrainProfile> source = new(() => SyntheticTrailRuns.Record(11));
    private static readonly Lazy<TerrainProfile> shifted = new(() => SyntheticTrailRuns.Record(11, leadInMetres: 15, recordingSeed: 24));

    [Fact]
    public void GroundSignalRecoversFifteenMetresOfGpsBias()
    {
        var aligned = Align(shifted.Value);
        Assert.All(aligned.Chunks, chunk => Assert.InRange(chunk.ShiftMetres, 12, 18));
        Assert.True(aligned.Pairs.Length > 80);
    }

    [Fact]
    public void ChangingReboundOnRoughSectionsPreservesAlignedGround()
    {
        var aligned = Align(shifted.Value);
        var changed = Align(SyntheticTrailRuns.Record(11, leadInMetres: 15, roughRebound: 0.7, recordingSeed: 24));
        Assert.Equal(aligned.Chunks.ToArray(), changed.Chunks.ToArray());
        Assert.Equal(aligned.Pairs.ToArray(), changed.Pairs.ToArray());
    }

    [Fact]
    public void UnrelatedGroundIsUnaligned()
    {
        var other = SyntheticTrailRuns.Record(91);
        Assert.IsType<TerrainRunAlignment.Unaligned>(TerrainRunAlignment.Calculate(source.Value, other,
            SyntheticTrailRuns.Map(source.Value, other)));
    }

    [Fact]
    public void DisjointSensorLocationsCannotAlign()
    {
        var frame = SyntheticTrailRuns.Record(11, fork: false);
        var fork = SyntheticTrailRuns.Record(11, frame: false);
        Assert.IsType<TerrainRunAlignment.NoCommonSensor>(TerrainRunAlignment.Calculate(frame, fork,
            SyntheticTrailRuns.Map(frame, fork)));
    }

    [Fact]
    public void FrameMissingFromOneRunUsesFork()
    {
        var aligned = Align(SyntheticTrailRuns.Record(11, frame: false));
        Assert.Equal(ImuLocation.Fork, aligned.Sensor);
        Assert.All(aligned.Chunks, chunk => Assert.InRange(chunk.ShiftMetres, -2, 2));
    }

    [Fact]
    public void ChunkAgreeingWithNoNeighbourIsLeftOutOfPairing()
    {
        // One chunk of ground lies 20 m further along than the rest, which lies 12 m further along.
        var ground = Texture(1);
        var displaced = Displace(ground, Texture(2), d => d is >= 610 and < 740 ? 32 : 12);
        var aligned = Assert.IsType<TerrainRunAlignment.Aligned>(TerrainRunAlignment.Calculate(ground, displaced, Identity(ground)));
        Assert.All(aligned.Chunks, chunk => Assert.Equal(12, chunk.ShiftMetres));
        Assert.All(aligned.Pairs, pair => Assert.Equal(WindowAt(ground.Windows[pair.SourceIndex].StartMetres + TerrainProfileCalculator.WindowMetres / 2d + 12), pair.MatchIndex));
    }

    [Fact]
    public void GroundSharedOverLessThanHalfTheChunksIsUnaligned()
    {
        var ground = Texture(1);
        var partly = Displace(ground, Texture(2), d => d < 450 ? 15 : null);
        Assert.IsType<TerrainRunAlignment.Unaligned>(TerrainRunAlignment.Calculate(ground, partly, Identity(ground)));
    }

    private const int TextureMetres = 1500;

    /// <summary>A 1500 m run at 10 m/s whose frame matching series is independent noise for each seed.</summary>
    private static TerrainProfile Texture(int seed)
    {
        var random = new Random(seed);
        return Run([.. Enumerable.Range(0, TextureMetres + 1).Select(_ => random.NextDouble())]);
    }

    /// <summary>The other run's series with each source metre written at its shift ahead, where the shift is given.</summary>
    private static TerrainProfile Displace(TerrainProfile ground, TerrainProfile other, Func<int, int?> shift)
    {
        var levels = other.FrameMatching.Select(s => s.LogLevel).ToArray();
        for (var d = 0; d <= TextureMetres; d++)
            if (shift(d) is { } s && d + s <= TextureMetres) levels[d + s] = ground.FrameMatching[d].LogLevel;
        return Run(levels);
    }

    private static TerrainProfile Run(double[] levels) =>
        TerrainProfiles.Profile(Enumerable.Range(0, TextureMetres / 10).Select(i => TerrainProfiles.Window(i))) with
        {
            FrameMatching = [.. levels.Select((level, metre) => new TerrainMatchSample(metre, metre / 10d, level))]
        };

    private static (double, double)[] Identity(TerrainProfile run) => [(0, 0), (run.Windows[^1].EndSeconds, run.Windows[^1].EndSeconds)];

    private static int WindowAt(double metres) => (int)(metres / TerrainProfileCalculator.WindowMetres);

    private static TerrainRunAlignment.Aligned Align(TerrainProfile other) =>
        Assert.IsType<TerrainRunAlignment.Aligned>(TerrainRunAlignment.Calculate(source.Value, other,
            SyntheticTrailRuns.Map(source.Value, other)));
}
