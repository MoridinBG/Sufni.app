using System.Collections.Immutable;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    private const double MinimumGradeMetres = 800;
    // Converts the travel difference into pitch. Real wheelbases lie within 8% of it, which errs by 0.35° at most.
    private const double NominalWheelbaseMillimetres = 1250;
    /// <summary>The boundaries between adjacent grade bands, in degrees downhill.</summary>
    public static IReadOnlyList<double> GradeBandEdges { get; } = [-6, -2, 2, 5, 10, 15];
    public const double GradeHysteresisDegrees = 1;
    private static readonly double[] BandEdges = [.. GradeBandEdges.Select(d => Sine(d))];
    private static readonly double[] UpperEdges = [.. GradeBandEdges.Select(d => Sine(d + GradeHysteresisDegrees))];
    private static readonly double[] LowerEdges = [.. GradeBandEdges.Select(d => Sine(d - GradeHysteresisDegrees))];

    private static double Sine(double degrees) => Math.Sin(degrees * Math.PI / 180);

    /// <summary>
    /// Measures the grade of the line through the axles for each moving window, cuts the grade pieces and gives each
    /// moving window the band of the piece that holds its midpoint. The grade is the recorded frame pitch less the
    /// pitch from the suspension travel, weighted by speed and not counted in the air.
    /// </summary>
    private static TerrainGradeState MeasureGrade(TrackMotion motion, RawImuData? imu, ImuMetaEntry? frame,
        ImmutableArray<Airtime> airtimes, Suspension? front, Suspension? rear, int suspensionRate,
        List<TerrainWindow> windows, List<TerrainWindow> cells, out ImmutableArray<TerrainWindow> pieces, CancellationToken token)
    {
        pieces = [];
        if (imu is null || frame is null || frame.GyroLsbPerDps <= 0) return TerrainGradeState.NoFrameSensor;
        var first = motion.At(windows.First(w => w.Moving).StartSeconds);
        var last = motion.At(windows.Last(w => w.Moving).EndSeconds);
        if (first?.Elevation is not { } startHeight || last?.Elevation is not { } endHeight)
            return TerrainGradeState.NoElevation;
        if (windows.Count(w => w.Moving) * WindowMetres < MinimumGradeMetres) return TerrainGradeState.TooShort;

        var segments = new List<(double Origin, double[] Sine, double[] Speed)>();
        foreach (var segment in imu.SampleSegments)
        {
            if (segment.LocationId != frame.LocationId || segment.Count == 0) continue;
            token.ThrowIfCancellationRequested();
            var origin = segment.FirstMonotonicDeltaUs / 1e6;
            var attitudes = FrameAttitudeEstimator.Recorded(segment, imu.SampleRate, frame, motion);
            var sine = new double[segment.Count];
            var speed = new double[segment.Count];
            var airtime = 0;
            for (var i = 0; i < segment.Count; i++)
            {
                var seconds = origin + (double)i / imu.SampleRate;
                while (airtime < airtimes.Length && airtimes[airtime].End < seconds) airtime++;
                var airborne = airtime < airtimes.Length && airtimes[airtime].Start <= seconds;
                speed[i] = airborne ? 0 : motion.At(seconds)?.Speed ?? 0;
                var framePitch = Math.Asin(Math.Clamp(-attitudes[i].Up.X, -1, 1));
                var suspensionPitch = TravelAt(front, suspensionRate, seconds) is { } frontTravel &&
                    TravelAt(rear, suspensionRate, seconds) is { } rearTravel
                    ? Math.Atan((frontTravel - rearTravel) / NominalWheelbaseMillimetres) : 0;
                sine[i] = Math.Sin(framePitch - suspensionPitch);
            }
            segments.Add((origin, sine, speed));
        }

        (double Sum, double Weight) Measure(TerrainWindow window)
        {
            foreach (var (origin, sine, speed) in segments)
            {
                if (!SampleRange(window.StartSeconds, window.EndSeconds, origin, imu.SampleRate, sine.Length, out var a, out var b))
                    continue;
                double sum = 0, weight = 0;
                for (var i = a; i < b; i++)
                {
                    sum += speed[i] * sine[i];
                    weight += speed[i];
                }
                return (sum, weight);
            }
            return (0, 0);
        }

        double? Mean(TerrainWindow window) => Measure(window) is { Weight: > 0 } measured ? measured.Sum / measured.Weight : null;

        double totalGrade = 0, totalWeight = 0;
        for (var j = 0; j < windows.Count; j++)
        {
            if (!windows[j].Moving || Measure(windows[j]) is not { Weight: > 0 } measured) continue;
            totalGrade += measured.Sum;
            totalWeight += measured.Weight;
            windows[j] = windows[j] with { Grade = measured.Sum / measured.Weight };
        }
        if (totalWeight == 0) return TerrainGradeState.NoFrameSensor;
        var zero = totalGrade / totalWeight - (startHeight - endHeight) / (last.Value.Distance - first.Value.Distance);
        for (var i = 0; i < windows.Count; i++)
            if (windows[i].Grade.HasValue) windows[i] = windows[i] with { Grade = windows[i].Grade - zero };

        var cut = CutPieces(cells, [[.. cells.Select(cell => cell.Moving && Mean(cell) is { } grade
            ? Math.Asin(grade) * 180 / Math.PI : (double?)null)]], GradePiecePenalty, GradeMinimumPieceCells);
        var grades = cut.Select(piece => Mean(piece) - zero).ToArray();
        var bands = AssignBands(grades, i => Adjacent(cut[i - 1], cut[i]));
        pieces = [.. cut.Select((piece, i) => piece with { Grade = grades[i], Band = bands[i] })];
        for (var i = 0; i < windows.Count; i++)
            if (windows[i].Moving && PieceAt(pieces, Midpoint(windows[i])) is { } piece)
                windows[i] = windows[i] with { Band = piece.Band };
        return TerrainGradeState.Available;
    }

    private static double? TravelAt(Suspension? side, int rate, double seconds)
    {
        if (side is null || rate <= 0) return null;
        foreach (var segment in SegmentsOf(side))
        {
            var position = (seconds - segment.StartSeconds) * rate;
            if (position < 0 || position > segment.SampleCount - 1) continue;
            var index = (int)position;
            var next = Math.Min(index + 1, segment.SampleCount - 1);
            return double.Lerp(side.Travel[segment.FirstDenseIndex + index], side.Travel[segment.FirstDenseIndex + next],
                position - index);
        }
        return null;
    }

    /// <summary>
    /// Bands of consecutive grades. Each edge keeps its side until the grade passes it by
    /// <see cref="GradeHysteresisDegrees"/>, so a grade near an edge does not switch the band on noise. The first
    /// window, a window after a gap and a window after one without a grade take the plain side of each edge.
    /// </summary>
    internal static GradeBand?[] AssignBands(IReadOnlyList<double?> grades, Func<int, bool> joinsPrevious)
    {
        var bands = new GradeBand?[grades.Count];
        var above = new bool[BandEdges.Length];
        var started = false;
        for (var i = 0; i < grades.Count; i++)
        {
            if (grades[i] is not { } grade)
            {
                started = false;
                continue;
            }
            if (!started || !joinsPrevious(i))
                for (var e = 0; e < BandEdges.Length; e++) above[e] = grade >= BandEdges[e];
            else
                for (var e = 0; e < BandEdges.Length; e++)
                    above[e] = above[e] ? grade >= LowerEdges[e] : grade >= UpperEdges[e];
            started = true;
            bands[i] = (GradeBand)above.Count(a => a);
        }
        return bands;
    }

    public static bool IsClimb(this GradeBand band) => band <= GradeBand.Climb;

    public static bool IsClimb(this RoughnessClass level) => level >= RoughnessClass.SmoothClimb;

    /// <summary>
    /// Median downhill grade over up to five adjacent windows, requiring three measured grades. A median keeps a
    /// change of grade at its place, where a mean would move it up to two windows early.
    /// </summary>
    internal static double? BaselineGrade<T>(IReadOnlyList<T> windows, int i, Func<T, double?> readGrade, Func<T, T, bool> adjacent)
    {
        if (readGrade(windows[i]) is not { } grade) return null;
        Span<double> grades = stackalloc double[5];
        grades[0] = grade;
        var count = 1;
        for (var direction = -1; direction <= 1; direction += 2)
        {
            for (var offset = 1; offset <= 2; offset++)
            {
                var j = i + direction * offset;
                if (j < 0 || j >= windows.Count) break;
                var lower = Math.Min(j, j - direction);
                if (!adjacent(windows[lower], windows[lower + 1])) break;
                if (readGrade(windows[j]) is { } neighbour) grades[count++] = neighbour;
            }
        }
        if (count < 3) return null;
        grades = grades[..count];
        grades.Sort();
        return count % 2 == 1 ? grades[count / 2] : (grades[count / 2 - 1] + grades[count / 2]) / 2;
    }
}
