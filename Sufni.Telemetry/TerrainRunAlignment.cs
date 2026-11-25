using System.Collections.Immutable;

namespace Sufni.Telemetry;

public readonly record struct TerrainAlignmentChunk(double CentreMetres, int ShiftMetres);
public readonly record struct TerrainWindowPair(int SourceIndex, int MatchIndex);

public closed record TerrainRunAlignment
{
    public sealed record Aligned(ImuLocation Sensor, ImmutableArray<TerrainAlignmentChunk> Chunks,
        ImmutableArray<TerrainWindowPair> Pairs) : TerrainRunAlignment;
    public sealed record NoCommonSensor : TerrainRunAlignment;
    public sealed record Unaligned : TerrainRunAlignment;

    public const int ChunkMetres = 150;
    public const int StepMetres = 75;
    public const int MaximumShiftMetres = 40;
    public const double MinimumCorrelation = 0.6;
    public const double MinimumCoverage = 0.8;
    public const double MinimumAcceptedShare = 0.5;
    public const int NeighbourShiftToleranceMetres = 5;

    public static TerrainRunAlignment Calculate(TerrainProfile source, TerrainProfile match,
        IReadOnlyList<(double SourceSeconds, double MatchSeconds)> timeMap, CancellationToken token = default)
    {
        var frame = !source.FrameMatching.IsEmpty && !match.FrameMatching.IsEmpty;
        if (!frame && (source.ForkMatching.IsEmpty || match.ForkMatching.IsEmpty)) return new NoCommonSensor();
        if (timeMap.Count < 2) return new Unaligned();
        IReadOnlyList<TerrainMatchSample> sourceSignal = frame ? source.FrameMatching : source.ForkMatching;
        IReadOnlyList<TerrainMatchSample> matchSignal = frame ? match.FrameMatching : match.ForkMatching;
        IReadOnlyList<TerrainWindow> sourceWindows = source.Windows, matchWindows = match.Windows;
        var first = source.Windows.FirstOrDefault(w => w.EndSeconds > timeMap[0].SourceSeconds);
        var last = source.Windows.LastOrDefault(w => w.StartSeconds < timeMap[^1].SourceSeconds);
        if (first is null || last is null) return new Unaligned();
        var start = MetresAt(first, Math.Max(first.StartSeconds, timeMap[0].SourceSeconds));
        var end = MetresAt(last, Math.Min(last.EndSeconds, timeMap[^1].SourceSeconds));
        var accepted = new List<TerrainAlignmentChunk>();
        var candidates = 0;
        var a = new double?[ChunkMetres];
        var b = new double?[ChunkMetres];
        for (var origin = start; origin + ChunkMetres <= end; origin += StepMetres)
        {
            token.ThrowIfCancellationRequested();
            for (var i = 0; i < ChunkMetres; i++)
            {
                a[i] = LevelAt(sourceSignal, origin + i);
                b[i] = Map(origin + i);
            }
            Correlation(0, out var covered);
            if (covered < MinimumCoverage * ChunkMetres) continue;
            candidates++;
            var best = double.NegativeInfinity;
            var shift = 0;
            for (var delta = -MaximumShiftMetres; delta <= MaximumShiftMetres; delta++)
            {
                if (Correlation(delta, out _) is not { } correlation || correlation <= best) continue;
                best = correlation;
                shift = delta;
            }
            if (best >= MinimumCorrelation && Math.Abs(shift) < MaximumShiftMetres)
                accepted.Add(new(origin + ChunkMetres / 2d, shift));
        }
        if (accepted.Count < MinimumAcceptedShare * candidates) return new Unaligned();
        var chunks = Consistent(accepted);
        if (chunks.Length < 2) return new Unaligned();

        var pairs = ImmutableArray.CreateBuilder<TerrainWindowPair>();
        for (var i = 0; i < source.Windows.Length; i++)
        {
            var window = source.Windows[i];
            var centre = window.StartMetres + TerrainProfileCalculator.WindowMetres / 2d;
            if (!window.Moving || centre < start || centre >= end || Map(centre) is not { } mapped) continue;
            var right = LowerBound(chunks, centre, static c => c.CentreMetres);
            var lower = chunks[Math.Max(0, right - 1)];
            var upper = chunks[Math.Min(right, chunks.Length - 1)];
            if (Math.Min(Math.Abs(centre - lower.CentreMetres), Math.Abs(centre - upper.CentreMetres)) > StepMetres) continue;
            var fraction = upper.CentreMetres == lower.CentreMetres ? 0 : (centre - lower.CentreMetres) / (upper.CentreMetres - lower.CentreMetres);
            var target = mapped + lower.ShiftMetres + fraction * (upper.ShiftMetres - lower.ShiftMetres);
            if (FindWindow(matchWindows, target, byTime: false) is { } j && match.Windows[j].Moving)
                pairs.Add(new(i, j));
        }
        return new Aligned(frame ? ImuLocation.Frame : ImuLocation.Fork, chunks, pairs.ToImmutable());

        double? Map(double distance)
        {
            if (FindWindow(sourceWindows, distance, byTime: false) is not { } sourceIndex) return null;
            var w = source.Windows[sourceIndex];
            var seconds = w.StartSeconds + (distance - w.StartMetres) / TerrainProfileCalculator.WindowMetres * (w.EndSeconds - w.StartSeconds);
            var j = LowerBound(timeMap, seconds, static p => p.SourceSeconds);
            double otherTime;
            if (j < timeMap.Count && timeMap[j].SourceSeconds == seconds) otherTime = timeMap[j].MatchSeconds;
            else
            {
                if (j == 0 || j == timeMap.Count) return null;
                var left = timeMap[j - 1];
                var right = timeMap[j];
                otherTime = left.MatchSeconds + (seconds - left.SourceSeconds) /
                    (right.SourceSeconds - left.SourceSeconds) * (right.MatchSeconds - left.MatchSeconds);
            }
            return FindWindow(matchWindows, otherTime, byTime: true) is { } k ? MetresAt(match.Windows[k], otherTime) : null;
        }

        double? Correlation(int shift, out int count)
        {
            double meanX = 0, meanY = 0, xx = 0, yy = 0, xy = 0;
            count = 0;
            for (var i = 0; i < ChunkMetres; i++)
            {
                if (a[i] is not { } x || b[i] is not { } d || LevelAt(matchSignal, d + shift) is not { } y) continue;
                count++;
                var dx = x - meanX;
                var dy = y - meanY;
                meanX += dx / count;
                meanY += dy / count;
                xx += dx * (x - meanX);
                yy += dy * (y - meanY);
                xy += dx * (y - meanY);
            }
            return count >= ChunkMetres * MinimumCoverage && xx > 0 && yy > 0 ? xy / Math.Sqrt(xx * yy) : null;
        }
    }

    private static ImmutableArray<TerrainAlignmentChunk> Consistent(List<TerrainAlignmentChunk> accepted) =>
    [
        .. accepted.Where((chunk, i) =>
            i > 0 && Math.Abs(chunk.ShiftMetres - accepted[i - 1].ShiftMetres) <= NeighbourShiftToleranceMetres ||
            i + 1 < accepted.Count && Math.Abs(chunk.ShiftMetres - accepted[i + 1].ShiftMetres) <= NeighbourShiftToleranceMetres)
    ];

    private static double MetresAt(TerrainWindow window, double time) => window.StartMetres +
        (time - window.StartSeconds) / (window.EndSeconds - window.StartSeconds) * TerrainProfileCalculator.WindowMetres;

    private static int? FindWindow(IReadOnlyList<TerrainWindow> windows, double coordinate, bool byTime)
    {
        var i = byTime ? LowerBound(windows, coordinate, static w => w.StartSeconds)
            : LowerBound(windows, coordinate, static w => w.StartMetres);
        if (i < windows.Count && (byTime ? windows[i].StartSeconds : windows[i].StartMetres) == coordinate) return i;
        if (i == 0) return null;
        var previous = windows[i - 1];
        return coordinate < (byTime ? previous.EndSeconds : previous.StartMetres + TerrainProfileCalculator.WindowMetres) ? i - 1 : null;
    }

    private static double? LevelAt(IReadOnlyList<TerrainMatchSample> series, double distance)
    {
        var i = LowerBound(series, distance, static p => p.Metres);
        if (i < series.Count && series[i].Metres == distance) return series[i].LogLevel;
        if (i == 0 || i == series.Count) return null;
        var previous = series[i - 1];
        var next = series[i];
        var width = next.Metres - previous.Metres;
        if (width > 1.000001) return null;
        return previous.LogLevel + (distance - previous.Metres) / width * (next.LogLevel - previous.LogLevel);
    }

    private static int LowerBound<T>(IReadOnlyList<T> values, double target, Func<T, double> coordinate)
    {
        int lo = 0, hi = values.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (coordinate(values[mid]) < target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
