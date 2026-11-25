using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>
/// A profile window with its ground class. The class value is the descent level in class steps on the setup scale;
/// a climb class has no class value and no source.
/// </summary>
public sealed record ClassifiedWindow(TerrainWindow Window, RoughnessClass? Roughness, GroundSensors? Source, double? ClassValue);

/// <summary>A profile with one classified window per profile window, in the same order.</summary>
public sealed record ClassifiedProfile(TerrainProfile Profile, TerrainScaleKey Scale, ImmutableArray<ClassifiedWindow> Windows);

/// <summary>Classes a profile on the scale of its setup. A sensor without a ready median sets no class.</summary>
public static class TerrainClassifier
{
    /// <summary>Covers the step, the levels, usable readings, the clipping order, the climb rule and the merge rule.</summary>
    public const int Version = 1;

    public static ClassifiedProfile Classify(TerrainProfile profile, TerrainScaleKey scale)
    {
        var windows = profile.Windows.Select(window => Classify(window, scale)).ToList();
        TerrainProfileCalculator.MergeOutliers(windows, static w => w.Roughness, static (w, level) => w with { Roughness = level },
            static (a, b) => a.Window.EndSeconds == b.Window.StartSeconds);
        return new(profile, scale, [.. windows]);
    }

    private static ClassifiedWindow Classify(TerrainWindow window, TerrainScaleKey scale)
    {
        if (!window.Moving) return new(window, null, null, null);
        if (window.Climbing)
        {
            RoughnessClass? climb = scale.FrameClimb is { } climbBin && window.Frame is { Level: > 0 } climbFrame
                ? climbFrame.Level < TerrainLevels.Median(climbBin) ? RoughnessClass.SmoothClimb : RoughnessClass.TechnicalClimb
                : null;
            return new(window, climb, null, null);
        }
        var fork = Usable(window.Fork, scale.ForkImu);
        var rearAxle = Usable(window.RearAxle, scale.RearAxleImu);
        var chosen = Unsprung(fork is { Clipped: false } ? fork : null, rearAxle is { Clipped: false } ? rearAxle : null)
            ?? (Usable(window.Frame, scale.FrameImu) is { } frame ? (frame.Value, GroundSensors.Frame) : ((double, GroundSensors)?)null)
            ?? Unsprung(fork, rearAxle);
        return chosen is (var value, var source)
            ? new(window, TerrainLevels.Classify(value), source, value)
            : new(window, null, null, null);
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
