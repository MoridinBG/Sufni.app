namespace Sufni.Telemetry.Tests;

public class TrailAverageTests
{
    private const int WindowCount = 180;
    private const double GroundGrade = 0.12;

    [Fact]
    public void FourIndependentGradeReadingsImproveOnEverySingleRideAcrossMostOfTheTrail()
    {
        var runs = new[] { 17, 53, 31, 29 }.Select(seed => Measurements(seed)).ToArray();
        var average = Average(runs);
        var improved = average.Windows.Count(w => Math.Abs(w.Grade!.Value - GroundGrade) <
            runs.Min(ride => Math.Abs(ride.Windows[w.SourceIndex].Grade!.Value - GroundGrade)));
        Assert.True(improved >= 0.80 * WindowCount, $"{improved} of {WindowCount} windows improved");
        Assert.All(average.Windows, w => Assert.Equal(SteepnessClass.Moderate, w.Steepness));
    }

    [Fact]
    public void GeometricMeanKeepsFivePercentAboveTheRoughBoundary()
    {
        var factors = new[] { 0.80, 1.25, 0.90, 1 / 0.90 };
        var runs = factors.Select(f => Measurements(17, frameLevel: 2.2 * 1.05 * f)).ToArray();
        var average = Average(runs);
        Assert.All(average.Windows, w =>
        {
            Assert.InRange(w.FrameLevel!.Value, 2.309, 2.311);
            Assert.Equal(RoughnessClass.Rough, w.Roughness);
        });
    }

    [Theory]
    [InlineData(3, RoughnessClass.Rough)]
    [InlineData(2, null)]
    public void UnknownScalesContributeOnlyToMeasuredLevel(int calibratedCount, RoughnessClass? expected)
    {
        var profiles = Enumerable.Range(0, 4).Select(i => i < calibratedCount
            ? Measurements(i, frameLevel: 2.4) : Measurements(i, frameLevel: 0.1, calibrated: false)).ToArray();
        var window = Average(profiles).Windows[0];
        Assert.Equal(expected, window.Roughness);
        Assert.InRange(window.FrameLevel!.Value, 0.48, 1.10);
    }

    [Theory]
    [InlineData(3, RoughnessClass.Rough)]
    [InlineData(2, RoughnessClass.Calm)]
    public void OnlyUnclippedLowersReadingsVoteAlongsideFrame(int unclipped, RoughnessClass expected)
    {
        var profiles = Enumerable.Range(0, 4).Select(i => Measurements(i, frameLevel: 1.32,
            lowersLevel: i < unclipped ? 7.5 : 0.1, clipped: i >= unclipped)).ToArray();
        var window = Average(profiles).Windows[0];
        Assert.Equal(expected, window.Roughness);
        Assert.NotNull(window.LowersLevel);
    }

    [Fact]
    public void GradeBaselineReachesPastAStop()
    {
        var gentle = Math.Sin(3 * Math.PI / 180);
        var windows = Enumerable.Range(0, 7).Select(i => i == 3 ? TerrainProfiles.Window(i, moving: false)
            : TerrainProfiles.Window(i) with { Grade = i < 3 ? gentle : 0.3 });
        var profile = TerrainProfiles.Profile(windows);
        var run = new AlignedTerrainRun(profile, TerrainProfiles.Identity(7));
        var average = Assert.IsType<TrailAverage.Averaged>(TrailAverage.Calculate(profile, new(0, 7), [run, run]));
        // Windows 0, 1, 2 and 4 give (3 × sin 3° + 0.3) / 4 ≈ 0.114, between sin 5° and sin 10°.
        Assert.Equal(SteepnessClass.Moderate, average.Windows.Single(w => w.SourceIndex == 2).Steepness);
    }

    [Fact]
    public void OneAlignedMatchCountsTwoRuns()
    {
        var profile = Measurements(12);
        var result = TrailAverage.Calculate(profile, new(0, WindowCount), [Aligned(Measurements(13))]);
        Assert.Equal(2, Assert.IsType<TrailAverage.TooFewRuns>(result).RunCount);
    }

    private static TrailAverage.Averaged Average(TerrainProfile[] profiles) =>
        Assert.IsType<TrailAverage.Averaged>(TrailAverage.Calculate(profiles[0], new(0, WindowCount),
            profiles.Skip(1).Select(Aligned).ToArray()));

    private static AlignedTerrainRun Aligned(TerrainProfile profile) => new(profile, TerrainProfiles.Identity(WindowCount));

    private static TerrainProfile Measurements(int seed, double frameLevel = 2.31, bool calibrated = true,
        double? lowersLevel = null, bool clipped = false)
    {
        var random = new Random(seed);
        var windows = Enumerable.Range(0, WindowCount).Select(i =>
        {
            var noise = (random.Next(2) == 0 ? -1 : 1) * (0.014 + 0.002 * random.NextDouble());
            return TerrainProfiles.Window(i) with
            {
                Grade = GroundGrade + noise,
                Frame = TerrainProfiles.Sensor(frameLevel, calibrated ? Math.Log(frameLevel / 1.4) / Math.Log(2.2 / 1.4) : null),
                Lowers = lowersLevel is { } l
                    ? TerrainProfiles.Sensor(l, Math.Log(l / 2.5) / Math.Log(3.8 / 2.5), clipped ? 0.10 : 0) : null
            };
        });
        return TerrainProfiles.Profile(windows, calibrated ? TerrainRoughnessState.Classified : TerrainRoughnessState.ValuesOnly,
            frameScale: calibrated ? 4096 : 2048, lowersScale: lowersLevel.HasValue ? 4096 : null);
    }
}
