namespace Sufni.Telemetry;

/// <summary>Mean travel of one end in mm on climbs and on descent bands; null where no window measured it.</summary>
public readonly record struct ClimbTravel(double? Climbing, double? Descending);

/// <summary>
/// The climbs in a time range. Bob (rear peak-to-peak mm) and cadence are time-weighted medians over the climb windows
/// with detected pedalling. Ascent is the net height gained on the climb windows.
/// </summary>
public sealed record ClimbingSummary(double DistanceMetres, double Seconds, double AscentMetres, double PedalledSeconds,
    double BobMillimetres, double CadenceRpm, ClimbTravel Front, ClimbTravel Rear, double SmoothMetres,
    double TechnicalMetres)
{
    public double PedalledShare => PedalledSeconds / Seconds;
}

public static class TerrainClimbing
{
    public const double MinimumPedalledSeconds = 120;

    /// <summary>Null when the climbs in the range hold less detected pedalling than the minimum.</summary>
    public static ClimbingSummary? Describe(ClassifiedProfile profile, TelemetryTimeRange? range)
    {
        double metres = 0, seconds = 0, ascent = 0, pedalled = 0, smooth = 0, technical = 0;
        var bob = new List<(double Value, double Weight)>();
        var cadence = new List<(double Value, double Weight)>();
        Mean frontClimbing = default, frontDescending = default, rearClimbing = default, rearDescending = default;
        foreach (var classified in profile.Windows)
        {
            var window = classified.Window;
            if (!window.Moving) continue;
            var fraction = window.CoveredFraction(range);
            if (fraction == 0) continue;
            var duration = fraction * (window.EndSeconds - window.StartSeconds);
            if (window.Band >= GradeBand.GentleDescent)
            {
                frontDescending.Add(duration, window.Front?.MeanTravel);
                rearDescending.Add(duration, window.Rear?.MeanTravel);
            }
            if (!window.Climbing) continue;
            var covered = fraction * TerrainProfileCalculator.WindowMetres;
            metres += covered;
            seconds += duration;
            ascent -= covered * (window.Grade ?? 0);
            if (classified.Roughness == RoughnessClass.SmoothClimb) smooth += covered;
            else if (classified.Roughness == RoughnessClass.TechnicalClimb) technical += covered;
            frontClimbing.Add(duration, window.Front?.MeanTravel);
            rearClimbing.Add(duration, window.Rear?.MeanTravel);
            if (window.Pedalling is not { Detected: true, BobMillimetres: { } windowBob, CadenceRpm: { } windowCadence })
                continue;
            pedalled += duration;
            bob.Add((windowBob, duration));
            cadence.Add((windowCadence, duration));
        }
        if (pedalled < MinimumPedalledSeconds) return null;
        return new(metres, seconds, Math.Max(0, ascent), pedalled, Median(bob), Median(cadence),
            new(frontClimbing.Value, frontDescending.Value), new(rearClimbing.Value, rearDescending.Value), smooth, technical);
    }

    private static double Median(List<(double Value, double Weight)> samples)
    {
        samples.Sort((a, b) => a.Value.CompareTo(b.Value));
        var half = samples.Sum(sample => sample.Weight) / 2;
        var cumulative = 0d;
        foreach (var (value, weight) in samples)
        {
            cumulative += weight;
            if (cumulative >= half) return value;
        }
        return samples[^1].Value;
    }

    private struct Mean
    {
        private double sum, weight;

        public void Add(double duration, double? value)
        {
            if (value is not { } v) return;
            sum += duration * v;
            weight += duration;
        }

        public readonly double? Value => weight > 0 ? sum / weight : null;
    }
}
