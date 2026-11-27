using System.Collections.Immutable;
using MathNet.Numerics.Statistics;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    public const int WindowMetres = 10;
    private const int CellMetres = 2;
    private const int CellsPerWindow = WindowMetres / CellMetres;
    private const double MinimumMovingSpeed = 1;
    private const double MinimumWindowSeconds = 0.4;

    /// <summary>
    /// Ten-metre windows from the start of each span, and the cells that split each window into equal distances. A
    /// cell moves with its window.
    /// </summary>
    private static List<TerrainWindow> CutWindows(TrackMotion motion, CancellationToken cancellationToken,
        out List<MatchingInterval> positions, out List<TerrainWindow> cells)
    {
        var windows = new List<TerrainWindow>();
        positions = [];
        cells = [];
        foreach (var span in motion.Spans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = span[0].Distance;
            var end = span[^1].Distance;
            var count = (int)Math.Floor((end - start + 1e-8) / WindowMetres);
            var startTime = span[0].Seconds;
            for (var i = 0; i < count; i++)
            {
                var metres = start + i * WindowMetres;
                var endTime = TimeAtDistance(span, metres + WindowMetres);
                var duration = endTime - startTime;
                var speed = WindowMetres / duration;
                var moving = speed > MinimumMovingSpeed && duration >= MinimumWindowSeconds;
                windows.Add(new(metres, startTime, endTime, speed, moving));
                var cellStart = startTime;
                for (var c = 1; c <= CellsPerWindow; c++)
                {
                    var cellEnd = c == CellsPerWindow ? endTime : TimeAtDistance(span, metres + c * CellMetres);
                    cells.Add(new(metres + (c - 1) * CellMetres, cellStart, cellEnd, CellMetres / (cellEnd - cellStart), moving));
                    cellStart = cellEnd;
                }
                startTime = endTime;
            }
            for (var i = 0; i <= end - start; i++)
            {
                var m = start + i;
                positions.Add(new(m, TimeAtDistance(span, m), TimeAtDistance(span, Math.Max(start, m - 3)),
                    TimeAtDistance(span, Math.Min(end, m + 3))));
            }
        }
        return windows;
    }

    private readonly record struct MatchingInterval(double Metres, double Seconds, double Start, double End);

    private static double TimeAtDistance(ImmutableArray<TrackMotionSample> grid, double target)
    {
        var low = 0;
        var high = grid.Length - 1;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (grid[mid].Distance < target) low = mid + 1;
            else high = mid;
        }
        if (low == 0) return grid[0].Seconds;
        var a = grid[low - 1];
        var b = grid[low];
        return a.Distance == b.Distance ? b.Seconds : double.Lerp(a.Seconds, b.Seconds,
            Math.Clamp((target - a.Distance) / (b.Distance - a.Distance), 0, 1));
    }

    private static bool SampleRange(double start, double end, double origin, int rate, int count, out int from, out int to)
    {
        from = (int)Math.Ceiling((start - origin) * rate - 1e-8);
        to = (int)Math.Ceiling((end - origin) * rate - 1e-8);
        return start >= origin - 1e-9 && end <= origin + (double)count / rate + 1e-9 && from >= 0 && to <= count && from < to;
    }

    private static double?[] MedianStrokeSizes(Suspension side, List<TerrainWindow> windows)
    {
        var medians = new double?[windows.Count];
        if (side.Strokes?.Compressions is not { IsDefaultOrEmpty: false } compressions) return medians;
        var strokes = compressions.Where(s => s.Start >= 0 && s.Start < side.Travel.Length)
            .OrderBy(s => s.StartSeconds).ToArray();
        var next = 0;
        var sizes = new List<double>();
        for (var i = 0; i < windows.Count; i++)
        {
            while (next < strokes.Length && strokes[next].StartSeconds < windows[i].StartSeconds) next++;
            sizes.Clear();
            for (var j = next; j < strokes.Length && strokes[j].StartSeconds < windows[i].EndSeconds; j++)
                sizes.Add(strokes[j].Stat.MaxTravel - side.Travel[strokes[j].Start]);
            if (sizes.Count > 0) medians[i] = sizes.Median();
        }
        return medians;
    }

    // Older processed telemetry stores continuous data with an empty segment list.
    private static ImmutableArray<ProcessedSuspensionSegment> SegmentsOf(Suspension side) => side.Segments.IsDefaultOrEmpty
        ? [new ProcessedSuspensionSegment { SampleCount = side.Travel.Length }] : side.Segments;

    private static void MeasureSuspension(Suspension? side, int rate, List<TerrainWindow> windows,
        bool front, CancellationToken token)
    {
        if (side is not { Present: true } || side.Velocity.IsDefaultOrEmpty || rate <= 0) return;
        var segments = SegmentsOf(side);
        var strokeSizes = MedianStrokeSizes(side, windows);
        foreach (var segment in segments)
        {
            for (var i = 0; i < windows.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var w = windows[i];
                if (!w.Moving || !SampleRange(w.StartSeconds, w.EndSeconds, segment.StartSeconds, rate, segment.SampleCount,
                        out var a, out var b)) continue;
                double power = 0, compression = 0, rebound = 0, travel = 0;
                for (var j = a; j < b; j++)
                {
                    var v = side.Velocity[segment.FirstDenseIndex + j];
                    power += v * v;
                    compression = Math.Max(compression, v);
                    rebound = Math.Max(rebound, -v);
                    travel += side.Travel[segment.FirstDenseIndex + j];
                }
                var figures = new SuspensionWindowFigures(Math.Sqrt(power / (b - a)), compression, rebound,
                    travel / (b - a), strokeSizes[i]);
                windows[i] = front ? w with { Front = figures } : w with { Rear = figures };
            }
        }
    }
}
