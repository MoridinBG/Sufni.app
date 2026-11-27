namespace Sufni.Telemetry.Tests;

public class TerrainClassifierTests
{
    private const double Clipped = 0.05;

    [Theory]
    [InlineData(-2, RoughnessClass.Smooth)]
    [InlineData(-1, RoughnessClass.Calm)]
    [InlineData(0, RoughnessClass.Medium)]
    [InlineData(1, RoughnessClass.Rough)]
    [InlineData(2, RoughnessClass.VeryRough)]
    public void ReadingAtTheMedianIsMediumAndEachStepMovesOneLevel(int steps, RoughnessClass expected)
    {
        var window = Single(Descent() with { Frame = Reading(steps) });
        Assert.Equal(expected, window.Roughness);
        Assert.Equal(GroundSensors.Frame, window.Source);
    }

    [Fact]
    public void UnsprungReadingsGiveTheMeanOfTheirValuesAndLeaveOutTheFrame()
    {
        var window = Single(Descent() with { Fork = Reading(0), RearAxle = Reading(2), Frame = Reading(-2) });
        Assert.Equal((RoughnessClass.Rough, GroundSensors.ForkAndRearAxle), (window.Roughness, window.Source));
        Assert.Equal(1.5, window.ClassValue!.Value, 9);
    }

    [Fact]
    public void FrameReadsTheGroundOnlyWithoutAnUnclippedUnsprungReading()
    {
        var window = Single(Descent() with { Fork = Reading(2, Clipped), RearAxle = Reading(2, Clipped), Frame = Reading(0) });
        Assert.Equal((RoughnessClass.Medium, GroundSensors.Frame), (window.Roughness, window.Source));
    }

    [Fact]
    public void UnclippedUnsprungReadingLeavesOutTheClippedOne()
    {
        var window = Single(Descent() with { Fork = Reading(2, Clipped), RearAxle = Reading(0), Frame = Reading(-2) });
        Assert.Equal((RoughnessClass.Medium, GroundSensors.RearAxle), (window.Roughness, window.Source));
    }

    [Fact]
    public void ClippedUnsprungReadingsReadTheGroundWithoutAFrameReading()
    {
        var window = Single(Descent() with { Fork = Reading(1, Clipped) });
        Assert.Equal((RoughnessClass.Rough, GroundSensors.Fork), (window.Roughness, window.Source));
    }

    [Fact]
    public void ZeroLevelReadingSetsNoClass()
    {
        var window = Single(Descent() with { Frame = TerrainProfiles.Sensor(0) });
        Assert.Equal(new ClassifiedWindow(window.Window, null, null, null, false), window);
    }

    [Fact]
    public void SensorWithoutAReadyMedianSetsNoClass()
    {
        var profile = TerrainProfiles.Profile([Descent() with { Fork = Reading(2), Frame = Reading(0) }]);
        var window = TerrainClassifier.Classify(profile, TerrainProfiles.Scale with { ForkImu = null }, TerrainLabelSet.None).Windows.Single();
        Assert.Equal((RoughnessClass.Medium, GroundSensors.Frame), (window.Roughness, window.Source));
    }

    [Theory]
    [InlineData(0.99, RoughnessClass.SmoothClimb)]
    [InlineData(1, RoughnessClass.TechnicalClimb)]
    public void ClimbBelowTheClimbMedianIsSmoothAndAtItTechnical(double ofMedian, RoughnessClass expected)
    {
        var window = Single(TerrainProfiles.Window(0) with
        {
            Band = GradeBand.Climb, Frame = TerrainProfiles.Sensor(ofMedian * TerrainLevels.Median(0))
        });
        Assert.Equal(new ClassifiedWindow(window.Window, expected, null, null, false), window);
    }

    [Theory]
    [InlineData(0, RoughnessClass.Calm)]
    [InlineData(0.5, RoughnessClass.Rough)]
    public void SingleWindowBetweenTwoOfAnotherClassTakesTheirClassOnlyWithoutAGap(double gapSeconds, RoughnessClass expected)
    {
        var windows = Enumerable.Range(0, 3).Select(i => TerrainProfiles.Window(i + (i == 2 ? gapSeconds : 0)) with
        {
            Frame = Reading(i == 1 ? 1 : -1)
        }).ToArray();
        var middle = TerrainProfiles.Classified(windows).Windows[1];
        Assert.Equal(expected, middle.Roughness);
        Assert.Equal(1.5, middle.ClassValue!.Value, 9);
    }

    [Fact]
    public void ScaleWithoutAReadyBinSetsNoClass()
    {
        var profile = TerrainProfiles.Profile([
            Descent() with { Fork = Reading(0), RearAxle = Reading(0), Frame = Reading(0) },
            TerrainProfiles.Window(1) with { Band = GradeBand.Climb, Frame = Reading(0) }
        ]);
        Assert.All(TerrainClassifier.Classify(profile, TerrainScaleKey.None, TerrainLabelSet.None).Windows, window =>
            Assert.Equal(new ClassifiedWindow(window.Window, null, null, null, false), window));
    }

    [Theory]
    [InlineData(0, RoughnessClass.Calm)]
    [InlineData(1, RoughnessClass.Medium)]
    [InlineData(2, RoughnessClass.Rough)]
    [InlineData(3, RoughnessClass.VeryRough)]
    public void EachEdgeStartsTheNextLevel(int edge, RoughnessClass above)
    {
        var level = TerrainLevels.Edges(0)[edge];
        Assert.Equal(above, Single(Descent() with { Frame = TerrainProfiles.Sensor(level * 1.0001) }).Roughness);
        Assert.Equal(above - 1, Single(Descent() with { Frame = TerrainProfiles.Sensor(level * 0.9999) }).Roughness);
    }

    [Fact]
    public void SummaryCountsTheDescentClassDistanceAndItsRoughShare()
    {
        // Gaps between the windows keep the outlier merge out of the classes.
        var profile = TerrainProfiles.Classified(
            TerrainProfiles.Window(0, RoughnessClass.Rough), TerrainProfiles.Window(2, RoughnessClass.VeryRough),
            TerrainProfiles.Window(4, RoughnessClass.Medium), TerrainProfiles.Window(6, RoughnessClass.Calm),
            TerrainProfiles.Climb(8), TerrainProfiles.Window(10), TerrainProfiles.Window(12, moving: false));
        Assert.Equal(new TerrainClassSummary(4 * TerrainProfileCalculator.WindowMetres, 0.5), TerrainClassifier.Summarize(profile));
    }

    [Fact]
    public void LabelledWindowHasTheLabelClassAndKeepsTheValueAndSourceOfItsReadings()
    {
        var profile = TerrainProfiles.Profile([Descent() with { Fork = Reading(0) }]);

        var unlabelled = TerrainClassifier.Classify(profile, TerrainProfiles.Scale, TerrainLabelSet.None).Windows.Single();
        var labelled = TerrainClassifier.Classify(profile, TerrainProfiles.Scale, Label(0, 1, RoughnessClass.VeryRough)).Windows.Single();

        Assert.Equal(unlabelled with { Roughness = RoughnessClass.VeryRough, Labelled = true }, labelled);
    }

    [Fact]
    public void SteepnessLabelSetsTheBandBeforeTheClass()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with
        {
            Band = GradeBand.Flat, Frame = TerrainProfiles.Sensor(TerrainLevels.Median(0) / 2)
        }]);

        var window = TerrainClassifier.Classify(profile, TerrainProfiles.Scale,
            TerrainProfiles.GradeLabels(new GradeLabelRange(0, 1, GradeBand.Climb))).Windows.Single();

        Assert.Equal((GradeBand.Climb, true, RoughnessClass.SmoothClimb), (window.Window.Band, window.GradeLabelled, window.Roughness));
    }

    [Fact]
    public void LabelSetsTheClassAfterTheOutlierMerge()
    {
        var profile = TerrainProfiles.Profile([.. Enumerable.Range(0, 3).Select(i => TerrainProfiles.Window(i, i == 1 ? RoughnessClass.Rough : RoughnessClass.Calm))]);

        var classes = TerrainClassifier.Classify(profile, TerrainProfiles.Scale, Label(1, 2, RoughnessClass.Rough)).Windows.Select(w => w.Roughness);

        Assert.Equal([RoughnessClass.Calm, RoughnessClass.Rough, RoughnessClass.Calm], classes);
    }

    [Fact]
    public void ScaleWithoutAReadyBinClassesOnlyTheLabelledWindows()
    {
        var profile = TerrainProfiles.Profile([Descent() with { Frame = Reading(0) }, TerrainProfiles.Window(1) with { Band = GradeBand.Descent, Frame = Reading(0) }]);

        var classes = TerrainClassifier.Classify(profile, TerrainScaleKey.None, Label(1, 2, RoughnessClass.Smooth)).Windows.Select(w => w.Roughness);

        Assert.Equal([null, RoughnessClass.Smooth], classes);
    }

    [Fact]
    public void SummaryCountsALabelledWindowWithItsLabel()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Calm), TerrainProfiles.Window(2, RoughnessClass.Calm)]);

        var summary = TerrainClassifier.Summarize(TerrainClassifier.Classify(profile, TerrainProfiles.Scale, Label(0, 1, RoughnessClass.Rough)));

        Assert.Equal(new TerrainClassSummary(2 * TerrainProfileCalculator.WindowMetres, 0.5), summary);
    }

    [Fact]
    public void SummaryOfARideWithoutDescentClassesIsEmpty()
    {
        var profile = TerrainProfiles.Classified(TerrainProfiles.Climb(0), TerrainProfiles.Window(2));
        Assert.Equal(new TerrainClassSummary(0, 0), TerrainClassifier.Summarize(profile));
    }

    [Theory]
    [InlineData(1, RoughnessClass.Rough, false)]
    [InlineData(2, RoughnessClass.VeryRough, false)]
    [InlineData(3, RoughnessClass.VeryRough, true)]
    [InlineData(-1, RoughnessClass.Calm, true)]
    public void StretchClashesWithItsLabelOnlyMoreThanOneClassOutsideIt(double steps, RoughnessClass reads, bool clashes)
    {
        var reading = TerrainClassifier.Read(TerrainProfiles.Stretch(RoughnessClass.Rough, TerrainProfiles.Steps(steps)), TerrainProfiles.Scale);

        Assert.Equal((reads, clashes), (reading!.Class, reading.Clashes));
    }

    [Fact]
    public void StretchReadsTheUnsprungSensorsBeforeTheFrame()
    {
        var stretch = TerrainProfiles.Stretch(RoughnessClass.Rough, TerrainProfiles.Steps(1), GroundSensors.Fork) with
        {
            FrameImu = new StretchLevel(TerrainProfiles.Steps(-2), 100)
        };

        Assert.Equal(RoughnessClass.Rough, TerrainClassifier.Read(stretch, TerrainProfiles.Scale)!.Class);
    }

    [Theory]
    [InlineData(-0.5, RoughnessClass.SmoothClimb, false)]
    [InlineData(2, RoughnessClass.TechnicalClimb, true)]
    public void ClimbStretchReadsAgainstTheClimbMedian(double steps, RoughnessClass reads, bool clashes)
    {
        var reading = TerrainClassifier.Read(TerrainProfiles.Stretch(RoughnessClass.SmoothClimb, TerrainProfiles.Steps(steps)), TerrainProfiles.Scale);

        Assert.Equal((reads, clashes), (reading!.Class, reading.Clashes));
    }

    [Theory]
    [InlineData(12, GradeBand.SteepDescent, false)]
    [InlineData(8.5, GradeBand.Descent, false)]
    [InlineData(7.5, GradeBand.Descent, true)]
    [InlineData(-3, GradeBand.Climb, true)]
    public void SteepnessStretchReadsTheBandOfItsMeanGradeAndClashesMoreThanTwoDegreesOutside(double degrees, GradeBand reads,
        bool clashes)
    {
        var stretch = new GradeStretch(new GradeLabelRange(0, 1, GradeBand.SteepDescent), Math.Sin(degrees * Math.PI / 180));

        var reading = TerrainClassifier.Read(stretch);

        Assert.Equal((reads, clashes), (reading.Band, reading.Clashes));
    }

    [Fact]
    public void StretchWithoutASensorOnTheScaleHasNoReading()
    {
        var stretch = TerrainProfiles.Stretch(RoughnessClass.Rough, TerrainProfiles.Steps(1));

        Assert.Null(TerrainClassifier.Read(stretch, TerrainProfiles.Scale with { FrameImu = null }));
    }

    private static TerrainWindow Descent() => TerrainProfiles.Window(0) with { Band = GradeBand.Descent };

    /// <summary>A reading the given number of class steps from the median of <see cref="TerrainProfiles.Scale"/>.</summary>
    private static SensorRoughness Reading(double steps, double clippedShare = 0) =>
        TerrainProfiles.Sensor(TerrainProfiles.Steps(steps), clippedShare: clippedShare);

    private static TerrainLabelSet Label(double start, double end, RoughnessClass roughness) =>
        TerrainProfiles.Labels(new TerrainLabelRange(start, end, roughness));

    private static ClassifiedWindow Single(TerrainWindow window) => TerrainProfiles.Classified(window).Windows.Single();
}
