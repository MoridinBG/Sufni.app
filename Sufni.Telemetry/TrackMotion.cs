using System.Collections.Immutable;

namespace Sufni.Telemetry;

public readonly record struct TrackSample(double Seconds, double East, double North, double? Elevation = null);

public readonly record struct TrackMotionSample(
    double Seconds, double East, double North, double? Elevation, double Speed, double Acceleration, double Distance);

/// <summary>Motion on a session-relative grid within each continuous span of original fixes.</summary>
public sealed class TrackMotion
{
    public const int GridRate = 10;
    private const double GapSeconds = 5;
    private const int MinimumFixes = 4;
    private const double SpeedCutoffHz = 0.4;

    public ImmutableArray<ImmutableArray<TrackMotionSample>> Spans { get; }

    private TrackMotion(ImmutableArray<ImmutableArray<TrackMotionSample>> spans) => Spans = spans;

    public static TrackMotion FromFixes(IEnumerable<TrackSample> fixes, double durationSeconds)
    {
        var sorted = fixes.Where(p => double.IsFinite(p.Seconds) && p.Seconds >= 0 && p.Seconds <= durationSeconds
                                      && double.IsFinite(p.East) && double.IsFinite(p.North))
            .DistinctBy(p => p.Seconds).OrderBy(p => p.Seconds).ToArray();
        var spans = ImmutableArray.CreateBuilder<ImmutableArray<TrackMotionSample>>();
        var distance = 0d;
        TrackSample? previousEnd = null;
        for (var start = 0; start < sorted.Length;)
        {
            var end = start + 1;
            while (end < sorted.Length && sorted[end].Seconds - sorted[end - 1].Seconds <= GapSeconds) end++;
            if (end - start >= MinimumFixes)
            {
                if (previousEnd is { } previous) distance += Separation(previous, sorted[start]);
                var span = BuildSpan(sorted[start..end], distance);
                spans.Add(span);
                var lastFix = sorted[end - 1];
                distance = span[^1].Distance + (lastFix.Seconds - span[^1].Seconds) * span[^1].Speed;
                previousEnd = lastFix;
            }
            start = end;
        }
        return new TrackMotion(spans.ToImmutable());
    }

    public TrackMotionSample? At(double seconds)
    {
        if (!double.IsFinite(seconds)) return null;
        var low = 0;
        var high = Spans.Length - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var span = Spans[mid];
            if (seconds < span[0].Seconds) high = mid - 1;
            else if (seconds > span[^1].Seconds) low = mid + 1;
            else
            {
                var index = Math.Min((int)((seconds - span[0].Seconds) * GridRate), span.Length - 1);
                var a = span[index];
                if (a.Seconds == seconds || index == span.Length - 1) return a;
                var b = span[index + 1];
                if (b.Seconds == seconds) return b;
                var weight = Math.Clamp((seconds - a.Seconds) * GridRate, 0, 1);
                return new TrackMotionSample(seconds, double.Lerp(a.East, b.East, weight),
                    double.Lerp(a.North, b.North, weight),
                    a.Elevation is { } e && b.Elevation is { } f ? double.Lerp(e, f, weight) : null,
                    double.Lerp(a.Speed, b.Speed, weight), double.Lerp(a.Acceleration, b.Acceleration, weight),
                    double.Lerp(a.Distance, b.Distance, weight));
            }
        }
        return null;
    }

    private static ImmutableArray<TrackMotionSample> BuildSpan(TrackSample[] fixes, double distance)
    {
        var times = fixes.Select(p => p.Seconds).ToArray();
        var east = fixes.Select(p => p.East).ToArray();
        var north = fixes.Select(p => p.North).ToArray();
        var heights = fixes.Where(p => p.Elevation is { } h && double.IsFinite(h)).ToArray();
        var heightTimes = heights.Select(p => p.Seconds).ToArray();
        var elevation = heights.Select(p => p.Elevation!.Value).ToArray();
        var midpoints = new double[times.Length - 1];
        var speeds = new double[midpoints.Length];
        for (var i = 0; i < midpoints.Length; i++)
        {
            midpoints[i] = (times[i] + times[i + 1]) / 2;
            speeds[i] = Separation(fixes[i], fixes[i + 1]) / (times[i + 1] - times[i]);
        }

        var length = checked((int)Math.Floor((times[^1] - times[0]) * GridRate) + 1);
        var gridSpeed = new double[length];
        for (var i = 0; i < length; i++)
            gridSpeed[i] = Interpolate(midpoints, speeds, times[0] + (double)i / GridRate);
        gridSpeed = SignalFilters.LowPass(gridSpeed, GridRate, SpeedCutoffHz);
        // A zero-phase filter can undershoot at a stop. Distance along the track must stay monotone.
        for (var i = 0; i < length; i++) gridSpeed[i] = Math.Max(0, gridSpeed[i]);

        var grid = ImmutableArray.CreateBuilder<TrackMotionSample>(length);
        for (var i = 0; i < length; i++)
        {
            var seconds = times[0] + (double)i / GridRate;
            var left = Math.Max(0, i - 1);
            var right = Math.Min(length - 1, i + 1);
            var acceleration = right == left ? 0 : (gridSpeed[right] - gridSpeed[left]) * GridRate / (right - left);
            if (i > 0) distance += (gridSpeed[i - 1] + gridSpeed[i]) / (2 * GridRate);
            double? height = heightTimes.Length > 0 && seconds >= heightTimes[0] && seconds <= heightTimes[^1]
                ? Interpolate(heightTimes, elevation, seconds) : null;
            grid.Add(new TrackMotionSample(seconds, Interpolate(times, east, seconds), Interpolate(times, north, seconds),
                height, gridSpeed[i], acceleration, distance));
        }
        return grid.MoveToImmutable();
    }

    private static double Interpolate(double[] times, double[] values, double time)
    {
        var i = Array.BinarySearch(times, time);
        if (i >= 0) return values[i];
        i = ~i;
        if (i == 0) return values[0];
        if (i == times.Length) return values[^1];
        return double.Lerp(values[i - 1], values[i], (time - times[i - 1]) / (times[i] - times[i - 1]));
    }

    private static double Separation(TrackSample a, TrackSample b) => double.Hypot(b.East - a.East, b.North - a.North);
}
