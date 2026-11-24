using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>
/// How the ground of one roughness level reached the rider. Activity is the RMS suspension velocity in mm/s and the
/// frame level the band-limited frame vibration in g, both as measured, so faster riding raises them.
/// </summary>
public sealed record ComfortRow(RoughnessClass Class, double DistanceMetres, double? FrontActivity, double? RearActivity,
    double? FrameLevel);

public static class TerrainComfort
{
    /// <summary>One row per roughness level with moving ground inside the range that the filter admits.</summary>
    public static ImmutableArray<ComfortRow> Describe(TerrainProfile profile, TelemetryTimeRange? range,
        Func<TerrainWindow, bool> admits)
    {
        var levels = Enum.GetValues<RoughnessClass>();
        var sums = new Sums[levels.Length];
        foreach (var window in profile.Windows)
        {
            if (!window.Moving || window.Roughness is not { } level || !admits(window)) continue;
            var fraction = window.CoveredFraction(range);
            if (fraction == 0) continue;
            sums[(int)level].Add(fraction, window.Front?.RmsVelocity, window.Rear?.RmsVelocity, window.Frame?.Level);
        }
        return [.. levels.Where(level => sums[(int)level].Fraction > 0).Select(level => sums[(int)level].Row(level))];
    }

    // Levels are RMS values, so a level's figure is the quadratic mean over its windows, weighted by covered time.
    private struct Sums
    {
        public double Fraction;
        private Quadratic front, rear, frame;

        public void Add(double fraction, double? frontValue, double? rearValue, double? frameValue)
        {
            Fraction += fraction;
            front.Add(fraction, frontValue);
            rear.Add(fraction, rearValue);
            frame.Add(fraction, frameValue);
        }

        public readonly ComfortRow Row(RoughnessClass level) =>
            new(level, Fraction * TerrainProfileCalculator.WindowMetres, front.Mean, rear.Mean, frame.Mean);
    }

    private struct Quadratic
    {
        private double squares, weight;

        public void Add(double fraction, double? value)
        {
            if (value is not { } v) return;
            squares += fraction * v * v;
            weight += fraction;
        }

        public readonly double? Mean => weight > 0 ? Math.Sqrt(squares / weight) : null;
    }
}
