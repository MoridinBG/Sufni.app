namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    private const double MinimumGradeMetres = 800;
    private const double GradeCutoffHz = 0.08;
    /// <summary>The boundaries between adjacent grade bands, in degrees downhill.</summary>
    public static IReadOnlyList<double> GradeBandEdges { get; } = [-6, -2, 2, 5, 10, 15];
    private static readonly double[] BandEdges = [.. GradeBandEdges.Select(d => Math.Sin(d * Math.PI / 180))];

    private static TerrainGradeState MeasureGrade(TrackMotion motion, RawImuData? imu, ImuMetaEntry? frame,
        List<TerrainWindow> windows, CancellationToken token)
    {
        if (imu is null || frame is null) return TerrainGradeState.NoFrameSensor;
        var first = motion.At(windows.First(w => w.Moving).StartSeconds);
        var last = motion.At(windows.Last(w => w.Moving).EndSeconds);
        if (first?.Elevation is not { } startHeight || last?.Elevation is not { } endHeight)
            return TerrainGradeState.NoElevation;
        if (windows.Count(w => w.Moving) * WindowMetres < MinimumGradeMetres) return TerrainGradeState.TooShort;

        double totalGrade = 0, totalWeight = 0;
        foreach (var segment in imu.SampleSegments)
        {
            if (segment.LocationId != frame.LocationId || segment.Count == 0) continue;
            token.ThrowIfCancellationRequested();
            var origin = segment.FirstMonotonicDeltaUs / 1e6;
            var x = new double[segment.Count];
            var z = new double[segment.Count];
            var speed = new double[segment.Count];
            for (var i = 0; i < segment.Count; i++)
            {
                var at = motion.At(origin + (double)i / imu.SampleRate);
                x[i] = segment[i].Ax / (double)frame.AccelLsbPerG - (at?.Acceleration ?? 0) / 9.80665;
                z[i] = segment[i].Az / (double)frame.AccelLsbPerG;
                speed[i] = at?.Speed ?? 0;
            }
            x = SignalFilters.LowPass(x, imu.SampleRate, GradeCutoffHz);
            z = SignalFilters.LowPass(z, imu.SampleRate, GradeCutoffHz);
            for (var j = 0; j < windows.Count; j++)
            {
                var window = windows[j];
                if (!window.Moving || !SampleRange(window.StartSeconds, window.EndSeconds, origin, imu.SampleRate,
                        segment.Count, out var a, out var b)) continue;
                double sum = 0, weight = 0;
                for (var i = a; i < b; i++)
                {
                    sum += speed[i] * Math.Sin(-Math.Atan2(x[i], z[i]));
                    weight += speed[i];
                }
                if (weight <= 0) continue;
                totalGrade += sum;
                totalWeight += weight;
                windows[j] = window with { Grade = sum / weight };
            }
        }
        if (totalWeight == 0) return TerrainGradeState.NoFrameSensor;
        var zero = totalGrade / totalWeight - (startHeight - endHeight) / (last.Value.Distance - first.Value.Distance);
        for (var i = 0; i < windows.Count; i++)
            if (windows[i].Grade.HasValue) windows[i] = windows[i] with { Grade = windows[i].Grade - zero };
        for (var i = 0; i < windows.Count; i++)
        {
            if (BaselineGrade(windows, i) is not { } baseline) continue;
            windows[i] = windows[i] with { Band = ClassifyGrade(baseline) };
        }
        MergeOutliers(windows, static w => w.Band, static (w, band) => w with { Band = band }, Adjacent);
        return TerrainGradeState.Available;
    }

    internal static GradeBand ClassifyGrade(double baseline)
    {
        var band = 0;
        while (band < BandEdges.Length && baseline >= BandEdges[band]) band++;
        return (GradeBand)band;
    }

    public static bool IsClimb(this GradeBand band) => band <= GradeBand.Climb;

    public static bool IsClimb(this RoughnessClass level) => level >= RoughnessClass.SmoothClimb;

    /// <summary>Mean downhill grade over up to five adjacent windows, requiring three measured grades.</summary>
    public static double? BaselineGrade(IReadOnlyList<TerrainWindow> windows, int i) =>
        BaselineGrade(windows, i, static w => w.Grade, static (a, b) => a.EndSeconds == b.StartSeconds);

    internal static double? BaselineGrade<T>(IReadOnlyList<T> windows, int i, Func<T, double?> readGrade, Func<T, T, bool> adjacent)
    {
        if (readGrade(windows[i]) is not { } grade) return null;
        var sum = grade;
        var count = 1;
        for (var direction = -1; direction <= 1; direction += 2)
        {
            for (var offset = 1; offset <= 2; offset++)
            {
                var j = i + direction * offset;
                if (j < 0 || j >= windows.Count) break;
                var lower = Math.Min(j, j - direction);
                if (!adjacent(windows[lower], windows[lower + 1])) break;
                if (readGrade(windows[j]) is { } neighbour) { sum += neighbour; count++; }
            }
        }
        return count < 3 ? null : sum / count;
    }
}
