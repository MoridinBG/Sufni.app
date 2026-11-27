namespace Sufni.Telemetry.Tests;

public static class TerrainProfiles
{
    /// <summary>The scale on which the readings of <see cref="Window(double, RoughnessClass, GroundSensors, double)"/> take their class.</summary>
    public static readonly TerrainScaleKey Scale = new(0, 0, 0, 0);

    public static ClassifiedProfile Classified(params TerrainWindow[] windows) => TerrainClassifier.Classify(Profile(windows), Scale, TerrainLabelSet.None);

    public static TerrainLabelSet Labels(params TerrainLabelRange[] ranges) => new([.. ranges]);

    public static TerrainProfile Profile(IEnumerable<TerrainWindow> windows,
        TerrainGradeState grade = TerrainGradeState.Available, double? frameScale = 4096, double? forkScale = 4096) =>
        new([.. windows], [], [], grade, frameScale, forkScale);

    /// <summary>The ten-metre window that starts at <paramref name="start"/> seconds and lasts one second.</summary>
    public static TerrainWindow Window(double start, bool moving = true) => new(10 * start, start, start + 1, 10, moving);

    /// <summary>
    /// A flat window whose readings take <paramref name="roughness"/> on <see cref="Scale"/> from
    /// <paramref name="source"/>. A climb class reads the frame, so the caller sets a climb band.
    /// </summary>
    public static TerrainWindow Window(double start, RoughnessClass roughness, GroundSensors source = GroundSensors.Frame,
        double grade = 0)
    {
        var sensor = Sensor(Level(roughness));
        return Window(start) with
        {
            Grade = grade, Band = GradeBand.Flat,
            Frame = source == GroundSensors.Frame || roughness.IsClimb() ? sensor : null,
            Fork = source is GroundSensors.ForkAndRearAxle or GroundSensors.Fork ? sensor : null,
            RearAxle = source is GroundSensors.ForkAndRearAxle or GroundSensors.RearAxle ? sensor : null
        };
    }

    /// <summary>A level at the centre of <paramref name="roughness"/> on <see cref="Scale"/>.</summary>
    public static double Level(RoughnessClass roughness) => TerrainLevels.Median(0) * Math.Pow(TerrainLevels.Step, roughness switch
    {
        RoughnessClass.Smooth => -2,
        RoughnessClass.Calm or RoughnessClass.SmoothClimb => -1,
        RoughnessClass.Medium => 0,
        RoughnessClass.Rough or RoughnessClass.TechnicalClimb => 1,
        _ => 2
    });

    /// <summary>
    /// A labelled stretch of <paramref name="metres"/> from <paramref name="start"/> seconds whose <paramref name="source"/>
    /// sensors read <paramref name="level"/>.
    /// </summary>
    public static LabelledStretch Stretch(RoughnessClass label, double level, GroundSensors source = GroundSensors.Frame,
        double start = 0, double metres = 100)
    {
        var reading = new StretchLevel(level, metres);
        return new(new TerrainLabelRange(start, start + 1, label),
            source == GroundSensors.Frame ? reading : null,
            source is GroundSensors.Fork or GroundSensors.ForkAndRearAxle ? reading : null,
            source is GroundSensors.RearAxle or GroundSensors.ForkAndRearAxle ? reading : null);
    }

    /// <summary>The level <paramref name="steps"/> class steps from the median of <paramref name="bin"/>.</summary>
    public static double Steps(double steps, int bin = 0) => TerrainLevels.Median(bin) * Math.Pow(TerrainLevels.Step, steps);

    /// <summary>A sensor reading with every band and the speed-removed value at <paramref name="level"/>.</summary>
    public static SensorRoughness Sensor(double level, double clippedShare = 0) =>
        new(level, level, level, level, level, clippedShare);

    /// <summary>Pairs each of the first <paramref name="windows"/> windows with the window of the same index.</summary>
    public static TerrainRunAlignment.Aligned Identity(int windows) =>
        new(ImuLocation.Frame, [], [.. Enumerable.Range(0, windows).Select(i => new TerrainWindowPair(i, i))]);

    /// <summary>Smooth climb windows at −0.1 grade with 50 mm mean rear travel and pedalling detected at 80 rpm.</summary>
    public static IEnumerable<TerrainWindow> PedalledClimb(int start, int count, double bob = 8) =>
        Enumerable.Range(start, count).Select(i => Climb(i, new PedallingFigures(true, 80, bob)));

    public static TerrainWindow Climb(int index, PedallingFigures? pedalling = null) => Window(index) with
    {
        Band = GradeBand.Climb, Grade = -0.1, Frame = Sensor(Level(RoughnessClass.SmoothClimb)), Pedalling = pedalling,
        Rear = new SuspensionWindowFigures(100, 1, 1, 50, null)
    };
}
