using System.Collections.Immutable;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    public const int WindowMetres = 10;
    private const double MinimumMovingSpeed = 2.5;
    private const double MinimumWindowSeconds = 0.4;

    private static List<TerrainWindow> CutWindows(TrackMotion motion, CancellationToken cancellationToken,
        out List<MatchingInterval> positions)
    {
        var windows = new List<TerrainWindow>();
        positions = [];
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
                windows.Add(new(metres, startTime, endTime, speed, speed > MinimumMovingSpeed && duration >= MinimumWindowSeconds));
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

    private static void MeasureSuspension(Suspension? side, int rate, List<TerrainWindow> windows,
        bool front, CancellationToken token)
    {
        if (side is not { Present: true } || side.Velocity.IsDefaultOrEmpty || rate <= 0) return;
        var segments = side.Segments.IsDefaultOrEmpty
            ? [new ProcessedSuspensionSegment { SampleCount = side.Velocity.Length }] : side.Segments;
        foreach (var segment in segments)
        {
            for (var i = 0; i < windows.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var w = windows[i];
                if (!w.Moving || !SampleRange(w.StartSeconds, w.EndSeconds, segment.StartSeconds, rate, segment.SampleCount,
                        out var a, out var b)) continue;
                double power = 0, compression = 0, rebound = 0;
                for (var j = a; j < b; j++)
                {
                    var v = side.Velocity[segment.FirstDenseIndex + j];
                    power += v * v;
                    compression = Math.Max(compression, v);
                    rebound = Math.Max(rebound, -v);
                }
                var figures = new SuspensionWindowFigures(Math.Sqrt(power / (b - a)), compression, rebound);
                windows[i] = front ? w with { Front = figures } : w with { Rear = figures };
            }
        }
    }
}
