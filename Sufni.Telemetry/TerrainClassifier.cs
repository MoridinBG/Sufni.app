using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>
/// A profile window with its ground class. The class value is the descent level in class steps on the setup scale;
/// a climb class has no class value and no source. A labelled window has the class of its label and keeps the class
/// value and the source of its readings.
/// </summary>
public sealed record ClassifiedWindow(TerrainWindow Window, RoughnessClass? Roughness, GroundSensors? Source, double? ClassValue, bool Labelled);

/// <summary>A profile with one classified window per profile window, in the same order.</summary>
public sealed record ClassifiedProfile(TerrainProfile Profile, TerrainScaleKey Scale, TerrainLabelSet Labels, ImmutableArray<ClassifiedWindow> Windows);

/// <summary>Ride class totals. <see cref="RoughShare"/> is the Rough and Very rough share of
/// <see cref="ClassifiedMetres"/>, the distance of the windows with a descent class.</summary>
public sealed record TerrainClassSummary(double ClassifiedMetres, double RoughShare);

/// <summary>
/// Classes a profile on the scale of its setup. A sensor without a ready median sets no class. A label sets the class
/// of the windows it holds, after the outlier merge.
/// </summary>
public static class TerrainClassifier
{
    /// <summary>Covers the step, the levels, usable readings, the clipping order, the climb rule, the merge rule and the label rule.</summary>
    public const int Version = 2;

    public static ClassifiedProfile Classify(TerrainProfile profile, TerrainScaleKey scale, TerrainLabelSet labels)
    {
        var windows = profile.Windows.Select(window => Classify(window, scale)).ToList();
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

    private readonly record struct Reading(double Value, bool Clipped);
}
