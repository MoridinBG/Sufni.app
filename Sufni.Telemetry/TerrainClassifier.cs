using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>
/// A profile window with its ground class. The class value is the descent level in class steps on the setup scale;
/// a climb class has no class value and no source. A labelled window has the class of its label and keeps the class
/// value and the source of its readings. A window with a steepness label holds the band of the label.
/// </summary>
public sealed record ClassifiedWindow(TerrainWindow Window, RoughnessClass? Roughness, GroundSensors? Source, double? ClassValue, bool Labelled)
{
    public bool GradeLabelled { get; init; }
}

/// <summary>A profile with one classified window per profile window, in the same order.</summary>
public sealed record ClassifiedProfile(TerrainProfile Profile, TerrainScaleKey Scale, TerrainLabelSet Labels, ImmutableArray<ClassifiedWindow> Windows);

/// <summary>Ride class totals. <see cref="RoughShare"/> is the Rough and Very rough share of
/// <see cref="ClassifiedMetres"/>, the distance of the windows with a descent class.</summary>
public sealed record TerrainClassSummary(double ClassifiedMetres, double RoughShare);

/// <summary>The class that a labelled stretch reads on a scale, and the class steps it lies outside the class of its label.</summary>
public sealed record LabelReading(RoughnessClass Class, double StepsOutside)
{
    public bool Clashes => StepsOutside > TerrainLabels.ClashSteps;
}

/// <summary>
/// The band that a steepness stretch reads, its mean grade in degrees, positive downhill, and the degrees it lies
/// outside the band of its label.
/// </summary>
public sealed record GradeReading(GradeBand Band, double Degrees, double DegreesOutside)
{
    public bool Clashes => DegreesOutside > TerrainLabels.GradeClashDegrees;
}

/// <summary>
/// Classes a profile on the scale of its setup. A sensor without a ready median sets no class. A steepness label sets
/// the band of the windows it holds before their class, so a window it makes a climb takes a climb class. A roughness
/// label sets the class of the windows it holds, after the outlier merge.
/// </summary>
public static class TerrainClassifier
{
    /// <summary>
    /// Covers the step, the levels, usable readings, the clipping order, the climb rule, the merge rule and the label
    /// rules.
    /// </summary>
    public const int Version = 3;

    public static ClassifiedProfile Classify(TerrainProfile profile, TerrainScaleKey scale, TerrainLabelSet labels)
    {
        var windows = profile.Windows.Select(window => labels.GradeAt(window) is { } grade
            ? Classify(window with { Band = grade.Band }, scale) with { GradeLabelled = true }
            : Classify(window, scale)).ToList();
        TerrainProfileCalculator.MergeOutliers(windows, static w => w.Roughness, static (w, level) => w with { Roughness = level },
            static (a, b) => a.Window.EndSeconds == b.Window.StartSeconds);
        return new(profile, scale, labels, [.. windows.Select(w =>
            labels.ClassAt(w.Window) is { } label ? w with { Roughness = label, Labelled = true } : w)]);
    }

    public static TerrainClassSummary Summarize(ClassifiedProfile profile)
    {
        var classified = profile.Windows.Where(w => w.Roughness is { } level && !level.IsClimb()).ToArray();
        var rough = classified.Count(w => w.Roughness is RoughnessClass.Rough or RoughnessClass.VeryRough);
        return new(TerrainProfileCalculator.WindowMetres * (double)classified.Length,
            classified.Length == 0 ? 0 : (double)rough / classified.Length);
    }

    /// <summary>
    /// How a labelled stretch reads on a scale, with the source order of a window: the fork and rear axle, then the frame.
    /// Null without a reading of a sensor that has a median.
    /// </summary>
    public static LabelReading? Read(LabelledStretch stretch, TerrainScaleKey scale)
    {
        var label = stretch.Label.Class;
        if (label.IsClimb())
        {
            if (scale.FrameClimb is not { } climbBin || stretch.FrameImu is not { Level: > 0 } climb) return null;
            var steps = Math.Log(climb.Level / TerrainLevels.Median(climbBin)) / Math.Log(TerrainLevels.Step);
            return new(steps < 0 ? RoughnessClass.SmoothClimb : RoughnessClass.TechnicalClimb,
                Math.Max(label == RoughnessClass.SmoothClimb ? steps : -steps, 0));
        }
        return (Unsprung(Usable(stretch.ForkImu, scale.ForkImu), Usable(stretch.RearAxleImu, scale.RearAxleImu))?.Value
                ?? Usable(stretch.FrameImu, scale.FrameImu)?.Value) is { } value
            ? new(TerrainLevels.Classify(value), TerrainLevels.StepsOutside(value, label))
            : null;
    }

    /// <summary>How a steepness stretch reads: the band of its mean grade on the plain band edges.</summary>
    public static GradeReading Read(GradeStretch stretch)
    {
        var degrees = Math.Asin(Math.Clamp(stretch.Grade, -1, 1)) * 180 / Math.PI;
        var edges = TerrainProfileCalculator.GradeBandEdges;
        var labelled = (int)stretch.Label.Band;
        var lower = labelled == 0 ? double.NegativeInfinity : edges[labelled - 1];
        var upper = labelled == edges.Count ? double.PositiveInfinity : edges[labelled];
        return new((GradeBand)edges.Count(edge => degrees >= edge), degrees, Math.Max(Math.Max(lower - degrees, degrees - upper), 0));
    }

    private static ClassifiedWindow Classify(TerrainWindow window, TerrainScaleKey scale)
    {
        if (!window.Moving) return new(window, null, null, null, false);
        if (window.Climbing)
        {
            RoughnessClass? climb = scale.FrameClimb is { } climbBin && window.Frame is { Level: > 0 } climbFrame
                ? climbFrame.Level < TerrainLevels.Median(climbBin) ? RoughnessClass.SmoothClimb : RoughnessClass.TechnicalClimb
                : null;
            return new(window, climb, null, null, false);
        }
        var fork = Usable(window.Fork, scale.ForkImu);
        var rearAxle = Usable(window.RearAxle, scale.RearAxleImu);
        var chosen = Unsprung(fork is { Clipped: false } ? fork : null, rearAxle is { Clipped: false } ? rearAxle : null)
            ?? (Usable(window.Frame, scale.FrameImu) is { } frame ? (frame.Value, GroundSensors.Frame) : ((double, GroundSensors)?)null)
            ?? Unsprung(fork, rearAxle);
        return chosen is (var value, var source)
            ? new(window, TerrainLevels.Classify(value), source, value, false)
            : new(window, null, null, null, false);
    }

    private static (double Value, GroundSensors Source)? Unsprung(Reading? fork, Reading? rearAxle) => (fork, rearAxle) switch
    {
        ({ } a, { } b) => ((a.Value + b.Value) / 2, GroundSensors.ForkAndRearAxle),
        ({ } a, null) => (a.Value, GroundSensors.Fork),
        (null, { } b) => (b.Value, GroundSensors.RearAxle),
        _ => null
    };

    private static Reading? Usable(SensorRoughness? reading, int? medianBin) =>
        reading is { Level: > 0 } && medianBin is { } bin
            ? new(TerrainLevels.Value(reading.SpeedRemoved, bin), reading.ClippedShare > TerrainProfileCalculator.ClippedShareLimit)
            : null;

    private static Reading? Usable(StretchLevel? stretch, int? medianBin) =>
        stretch is { Level: > 0 } && medianBin is { } bin ? new(TerrainLevels.Value(stretch.Level, bin), false) : null;

    private readonly record struct Reading(double Value, bool Clipped);
}
