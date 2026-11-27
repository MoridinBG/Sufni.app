namespace Sufni.Telemetry.Tests;

public class TrailAverageTests
{
    private const int WindowCount = 180;
    private const double GroundGrade = 0.12;

    private static readonly int[] gradeSeeds = [17, 53, 31, 29];

    [Fact]
    public void FourIndependentGradeReadingsImproveOnEverySingleRideAcrossMostOfTheTrail()
    {
        var runs = gradeSeeds.Select(seed => Measurements(seed)).ToArray();
        var average = Average(runs.Select(ride => Run(ride)));
        var improved = average.Windows.Count(w => Math.Abs(w.Grade!.Value - GroundGrade) <
            runs.Min(ride => Math.Abs(ride.Windows[w.SourceIndex].Grade!.Value - GroundGrade)));
        Assert.True(improved >= 0.80 * WindowCount, $"{improved} of {WindowCount} windows improved");
    }

    [Fact]
    public void AveragedGradeBetweenFiveAndTenDegreesIsDescent()
    {
        var average = Average(gradeSeeds.Select(seed => Run(Measurements(seed))));
        Assert.All(average.Windows, w => Assert.Equal(GradeBand.Descent, w.Band));
    }

    // Runs past the classed ones have no climb median, so they set no climb class.
    [Theory]
    [InlineData(2, 2, RoughnessClass.TechnicalClimb)]
    [InlineData(1, 3, RoughnessClass.SmoothClimb)]
    [InlineData(1, 1, null)]
    public void AveragedClimbTakesTheMajorityClimbClassOfAtLeastThreeRuns(int technical, int smooth, RoughnessClass? expected)
    {
        var average = Average(gradeSeeds.Select((seed, i) => Run(Measurements(seed, grade: -GroundGrade, band: GradeBand.SteepClimb,
            frameLevel: TerrainProfiles.Level(i < technical ? RoughnessClass.TechnicalClimb : RoughnessClass.SmoothClimb)),
            i < technical + smooth ? TerrainProfiles.Scale : TerrainScaleKey.None)));
        Assert.All(average.Windows, w =>
        {
            Assert.Equal(GradeBand.SteepClimb, w.Band);
            Assert.Equal(expected, w.Roughness);
        });
    }

    [Fact]
    public void MeanClassValueKeepsFivePercentAboveTheRoughBoundary()
    {
        var boundary = TerrainLevels.Median(0) * Math.Sqrt(TerrainLevels.Step);
        var factors = new[] { 0.80, 1.25, 0.90, 1 / 0.90 };
        var average = Average(factors.Select(f => Run(Measurements(17, frameLevel: boundary * 1.05 * f))));
        Assert.All(average.Windows, w =>
        {
            Assert.Equal(boundary * 1.05, w.FrameLevel!.Value, 6);
            Assert.Equal(RoughnessClass.Rough, w.Roughness);
        });
    }

    // The measured level is the geometric mean of all four readings: (2.4³ × 0.1)^¼ and (2.4² × 0.1²)^¼.
    [Theory]
    [InlineData(3, RoughnessClass.VeryRough, 1.0843)]
    [InlineData(2, null, 0.4899)]
    public void RunsWithoutAClassValueContributeOnlyToTheMeasuredLevel(int classed, RoughnessClass? expected, double level)
    {
        var window = Average(Enumerable.Range(0, 4).Select(i => i < classed
            ? Run(Measurements(i, frameLevel: 2.4)) : Run(Measurements(i, frameLevel: 0.1), TerrainScaleKey.None))).Windows[0];
        Assert.Equal(expected, window.Roughness);
        Assert.Equal(level, window.FrameLevel!.Value, 0.0005);
    }

    // Three readings of 1, 2 and 4 g give the geometric mean 2 g; two readings give no level.
    [Theory]
    [InlineData(3, 2.0)]
    [InlineData(2, null)]
    public void RearAxleLevelIsTheGeometricMeanOfAtLeastThreeReadings(int readings, double? expected)
    {
        double[] levels = [1, 2, 4];
        var profiles = Enumerable.Range(0, 4).Select(i => Measurements(i, rearAxleLevel: i < readings ? levels[i] : null)).ToArray();
        var level = Average(profiles.Select(ride => Run(ride))).Windows[0].RearAxleLevel;
        if (expected is { } value) Assert.Equal(value, level!.Value, 0.0005);
        else Assert.Null(level);
    }

    // Readings of 1, 2 and 4 g give the geometric mean 2 g.
    [Theory]
    [InlineData(4, 0.03)]
    [InlineData(0, 0)]
    public void ClippedOrZeroReadingStaysOutOfTheMeasuredLevels(double level, double clippedShare)
    {
        double[] levels = [1, 2, 4];
        var window = Average(levels.Select(l => TerrainProfiles.Sensor(l)).Append(TerrainProfiles.Sensor(level, clippedShare))
            .Select(reading => Run(Readings(reading)))).Windows[0];
        Assert.Equal(2, window.FrameLevel!.Value, 0.0005);
        Assert.Equal(2, window.ForkLevel!.Value, 0.0005);
    }

    [Fact]
    public void TwoEligibleReadingsAndAClippedOneGiveNoMeasuredLevel()
    {
        SensorRoughness[] readings = [TerrainProfiles.Sensor(1), TerrainProfiles.Sensor(2), TerrainProfiles.Sensor(4, clippedShare: 0.03)];
        var window = Average(readings.Select(reading => Run(Readings(reading)))).Windows[0];
        Assert.Null(window.FrameLevel);
        Assert.Null(window.ForkLevel);
    }

    [Fact]
    public void GradeBaselineReachesPastAStop()
    {
        var windows = Enumerable.Range(0, 7).Select(i => i == 3 ? TerrainProfiles.Window(i, moving: false)
            : TerrainProfiles.Window(i) with { Grade = i < 2 ? 0 : 0.3 });
        var profile = TerrainProfiles.Profile(windows);
        var run = new AlignedTerrainRun(Run(profile), TerrainProfiles.Identity(7));
        var average = Assert.IsType<TrailAverage.Averaged>(TrailAverage.Calculate(Run(profile), new(0, 7), [run, run]));
        // Windows 0, 1 and 2 alone give the median 0. With window 4 the median is (0 + 0.3) / 2 = 0.15,
        // between sin 5° and sin 10°.
        Assert.Equal(GradeBand.Descent, average.Windows.Single(w => w.SourceIndex == 2).Band);
    }

    [Theory]
    [InlineData(1.5, GradeBand.GentleDescent)]
    [InlineData(0.9, GradeBand.Flat)]
    [InlineData(10.5, GradeBand.Descent)]
    [InlineData(11.1, GradeBand.SteepDescent)]
    public void GradeBandChangesOnlyWhenTheGradePassesAnEdgeByOneDegree(double degrees, GradeBand expected)
    {
        var bands = GradeBands([(10, 3), (10, degrees)]);
        Assert.Equal(GradeBand.GentleDescent, bands[5]);
        Assert.Equal(expected, bands[15]);
    }

    [Fact]
    public void GradeBandStartsFromThePlainEdgesAfterAGap()
    {
        var bands = GradeBands([(10, 3), (10, 1.5)], gapSeconds: 0.5);
        Assert.Equal(GradeBand.Flat, bands[15]);
    }

    [Fact]
    public void GradeBandStartsFromThePlainEdgesAfterAWindowWithoutBaseline()
    {
        var bands = GradeBands([(10, 3), (1, double.NaN), (10, 1.5)]);
        Assert.Equal(GradeBand.Flat, bands[16]);
    }

    [Theory]
    [InlineData(0, RoughnessClass.Calm)]
    [InlineData(0.5, RoughnessClass.Rough)]
    public void SingleRoughWindowJoinsCalmNeighboursOnlyWithoutAGap(double gapSeconds, RoughnessClass expected)
    {
        var windows = Enumerable.Range(0, 5).Select(i => TerrainProfiles.Window(i + (i >= 2 ? gapSeconds : 0)) with
        {
            Frame = TerrainProfiles.Sensor(TerrainProfiles.Level(i == 2 ? RoughnessClass.Rough : RoughnessClass.Calm))
        });
        var profile = TerrainProfiles.Profile(windows);
        var run = new AlignedTerrainRun(Run(profile), TerrainProfiles.Identity(5));
        var average = Assert.IsType<TrailAverage.Averaged>(TrailAverage.Calculate(Run(profile), new(0, 6), [run, run]));
        Assert.Equal(expected, average.Windows.Single(w => w.SourceIndex == 2).Roughness);
    }

    [Fact]
    public void OneAlignedMatchCountsTwoRuns()
    {
        var profile = Measurements(12);
        var result = TrailAverage.Calculate(Run(profile), new(0, WindowCount),
            [new AlignedTerrainRun(Run(Measurements(13)), TerrainProfiles.Identity(WindowCount))]);
        Assert.Equal(2, Assert.IsType<TrailAverage.TooFewRuns>(result).RunCount);
    }

    [Fact]
    public void LabelledSourceWindowKeepsItsLabelClass()
    {
        var runs = gradeSeeds.Select(seed => Run(Measurements(seed))).ToArray();
        var unlabelled = Average(runs);

        var labelled = Average([TerrainClassifier.Classify(runs[0].Profile, TerrainProfiles.Scale, FirstTenSeconds), .. runs.Skip(1)]);

        Assert.All(labelled.Windows.Take(10), w => Assert.Equal(RoughnessClass.Smooth, w.Roughness));
        Assert.Equal(unlabelled.Windows.Skip(10), labelled.Windows.Skip(10));
    }

    [Fact]
    public void SteepnessLabelledSourceWindowKeepsItsLabelBand()
    {
        var runs = gradeSeeds.Select(seed => Run(Measurements(seed))).ToArray();
        var labels = TerrainProfiles.GradeLabels(new GradeLabelRange(0, 10, GradeBand.VerySteepDescent));

        var labelled = Average([TerrainClassifier.Classify(runs[0].Profile, TerrainProfiles.Scale, labels), .. runs.Skip(1)]);

        Assert.All(labelled.Windows.Take(10), w => Assert.Equal(GradeBand.VerySteepDescent, w.Band));
    }

    [Fact]
    public void LabelOfAMatchedRideHasNoEffect()
    {
        var runs = gradeSeeds.Select(seed => Run(Measurements(seed))).ToArray();
        var unlabelled = Average(runs);

        var labelled = Average([runs[0], TerrainClassifier.Classify(runs[1].Profile, TerrainProfiles.Scale, FirstTenSeconds), .. runs.Skip(2)]);

        Assert.Equal(unlabelled.Windows, labelled.Windows);
    }

    private static readonly TerrainLabelSet FirstTenSeconds = TerrainProfiles.Labels(new TerrainLabelRange(0, 10, RoughnessClass.Smooth));

    /// <summary>
    /// The averaged bands of three equal runs over consecutive stretches of constant grade in degrees. A NaN stretch
    /// has no grade. The gap goes before the second stretch.
    /// </summary>
    private static GradeBand?[] GradeBands((int Windows, double Degrees)[] stretches, double gapSeconds = 0)
    {
        var grades = stretches.SelectMany(s => Enumerable.Repeat(s.Degrees, s.Windows)).ToArray();
        var windows = grades.Select((degrees, i) => TerrainProfiles.Window(i + (i >= stretches[0].Windows ? gapSeconds : 0)) with
        {
            Grade = double.IsNaN(degrees) ? null : Math.Sin(degrees * Math.PI / 180)
        });
        var run = Run(TerrainProfiles.Profile(windows));
        var aligned = new AlignedTerrainRun(run, TerrainProfiles.Identity(grades.Length));
        var average = Assert.IsType<TrailAverage.Averaged>(TrailAverage.Calculate(run, new(0, grades.Length + 1), [aligned, aligned]));
        return [.. average.Windows.Select(w => w.Band)];
    }

    private static ClassifiedProfile Run(TerrainProfile profile, TerrainScaleKey? scale = null) =>
        TerrainClassifier.Classify(profile, scale ?? TerrainProfiles.Scale, TerrainLabelSet.None);

    private static TrailAverage.Averaged Average(IEnumerable<ClassifiedProfile> runs)
    {
        var all = runs.ToArray();
        return Assert.IsType<TrailAverage.Averaged>(TrailAverage.Calculate(all[0], new(0, WindowCount),
            [.. all.Skip(1).Select(run => new AlignedTerrainRun(run, TerrainProfiles.Identity(WindowCount)))]));
    }

    private static TerrainProfile Readings(SensorRoughness reading) =>
        TerrainProfiles.Profile(Enumerable.Range(0, WindowCount).Select(i => TerrainProfiles.Window(i) with { Frame = reading, Fork = reading }));

    private static TerrainProfile Measurements(int seed, double frameLevel = 2.31, double grade = GroundGrade,
        GradeBand? band = null, double? rearAxleLevel = null)
    {
        var random = new Random(seed);
        var windows = Enumerable.Range(0, WindowCount).Select(i =>
        {
            var noise = (random.Next(2) == 0 ? -1 : 1) * (0.014 + 0.002 * random.NextDouble());
            return TerrainProfiles.Window(i) with
            {
                Grade = grade + noise,
                Band = band,
                Frame = TerrainProfiles.Sensor(frameLevel),
                RearAxle = rearAxleLevel is { } r ? TerrainProfiles.Sensor(r) : null
            };
        });
        return TerrainProfiles.Profile(windows);
    }
}
