using System.Numerics;

namespace Sufni.Telemetry;

public enum GpsTimeOffsetUnavailableReason { NoFrameGyro, NoTrack, TooLittleMovement }

public closed record GpsTimeOffsetEstimate
{
    private GpsTimeOffsetEstimate() { }
    public sealed record Measured(double LagSeconds, double Correlation) : GpsTimeOffsetEstimate
    {
        public bool ShouldSuggest => Correlation >= 0.65 && Math.Abs(LagSeconds) > 0.5;
    }
    public sealed record Unavailable(GpsTimeOffsetUnavailableReason Reason) : GpsTimeOffsetEstimate;
}

public static class GpsTimeOffsetEstimator
{
    private const int Rate = TrackMotion.GridRate;
    private const int MaxLag = 15 * Rate;
    private const int MinimumSeconds = 30;
    private const int MinimumMovingCells = 300;
    private const int MinimumCommonCells = 200;
    private const double CutoffHz = 0.25;
    private const double MinimumSpeed = 3;
    private const double MaxCourseRate = 150;

    public static GpsTimeOffsetEstimate Estimate(TelemetryData telemetry, IReadOnlyList<TrackSample> fixes,
        CancellationToken cancellationToken = default)
    {
        var imu = telemetry.ImuData;
        var meta = imu?.Meta.FirstOrDefault(m => m.LocationId == (byte)ImuLocation.Frame && m.AccelLsbPerG > 0 && m.GyroLsbPerDps > 0);
        if (imu is null || meta is null || imu.SampleRate <= 0 || !imu.ActiveLocations.Contains(meta.LocationId))
            return new GpsTimeOffsetEstimate.Unavailable(GpsTimeOffsetUnavailableReason.NoFrameGyro);
        var motion = TrackMotion.FromFixes(fixes, telemetry.Metadata.Duration);
        if (motion.Spans.IsEmpty) return new GpsTimeOffsetEstimate.Unavailable(GpsTimeOffsetUnavailableReason.NoTrack);

        var spans = motion.Spans;
        var origin = spans[0][0].Seconds;
        var length = checked((int)Math.Round((spans[^1][0].Seconds - origin) * Rate) + spans[^1].Length);
        var yaw = new double[length];
        var courseRate = new double[length];
        var samples = new int[length];
        var moving = new bool[length];
        foreach (var segment in imu.SampleSegments)
        {
            if (segment.LocationId != meta.LocationId) continue;
            Quaternion? rotation = null;
            var spanIndex = 0;
            for (var i = 0; i < segment.Count; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var time = segment.FirstMonotonicDeltaUs / 1e6 + (double)i / imu.SampleRate;
                var record = segment[i];
                rotation = FrameAttitudeEstimator.Advance(rotation, record, 1d / imu.SampleRate, meta.AccelLsbPerG, meta.GyroLsbPerDps);
                while (spanIndex < spans.Length && time > spans[spanIndex][^1].Seconds) spanIndex++;
                if (spanIndex == spans.Length) break;
                var first = spans[spanIndex][0].Seconds;
                if (time < first) continue;
                var cell = (int)Math.Round((first - origin) * Rate) + (int)Math.Floor((time - first) * Rate);
                yaw[cell] += Vector3.Dot(new Vector3(record.Gx, record.Gy, record.Gz), FrameAttitudeEstimator.Read(rotation.Value).Up) / meta.GyroLsbPerDps;
                samples[cell]++;
            }
        }

        foreach (var span in spans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (span.Length < 2) continue;
            var start = (int)Math.Round((span[0].Seconds - origin) * Rate);
            var east = SignalFilters.LowPass(span.Select(p => p.East).ToArray(), Rate, CutoffHz);
            var north = SignalFilters.LowPass(span.Select(p => p.North).ToArray(), Rate, CutoffHz);
            var heading = new double[span.Length];
            for (var i = 0; i < heading.Length; i++)
            {
                var dx = Derivative(east, i);
                var dy = Derivative(north, i);
                var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                heading[i] = i == 0 ? angle : heading[i - 1] + Math.IEEERemainder(angle - heading[i - 1], 360);
                var cell = start + i;
                moving[cell] = samples[cell] > 0 && double.Hypot(dx, dy) > MinimumSpeed;
                if (samples[cell] > 0) yaw[cell] /= samples[cell];
            }
            // Missing IMU cells also bound the yaw filter, so a dropout cannot bias its neighbours.
            for (var i = 0; i < span.Length;)
            {
                if (samples[start + i] == 0) { i++; continue; }
                var end = i + 1;
                while (end < span.Length && samples[start + end] > 0) end++;
                SignalFilters.LowPass(yaw.AsSpan(start + i, end - i), Rate, CutoffHz).CopyTo(yaw, start + i);
                i = end;
            }
            for (var i = 0; i < span.Length; i++)
            {
                if (moving[start + i]) courseRate[start + i] = Math.Clamp(Derivative(heading, i), -MaxCourseRate, MaxCourseRate);
                else yaw[start + i] = 0;
            }
        }
        if (length < MinimumSeconds * Rate || moving.Count(m => m) < MinimumMovingCells)
            return new GpsTimeOffsetEstimate.Unavailable(GpsTimeOffsetUnavailableReason.TooLittleMovement);

        var scores = new double[2 * MaxLag + 1];
        var best = -1;
        for (var lag = -MaxLag; lag <= MaxLag; lag++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = lag + MaxLag;
            scores[index] = Correlation(yaw, courseRate, moving, lag);
            if (double.IsFinite(scores[index]) && (best < 0 || scores[index] > scores[best])) best = index;
        }
        if (best < 0) return new GpsTimeOffsetEstimate.Unavailable(GpsTimeOffsetUnavailableReason.TooLittleMovement);
        var offset = 0d;
        if (best > 0 && best < scores.Length - 1)
        {
            var curvature = scores[best - 1] - 2 * scores[best] + scores[best + 1];
            if (double.IsFinite(curvature) && curvature < 0)
                offset = Math.Clamp(0.5 * (scores[best - 1] - scores[best + 1]) / curvature, -0.5, 0.5);
        }
        return new GpsTimeOffsetEstimate.Measured((best - MaxLag + offset) / Rate, scores[best]);
    }

    private static double Derivative(double[] values, int i)
    {
        var a = Math.Max(0, i - 1);
        var b = Math.Min(values.Length - 1, i + 1);
        return (values[b] - values[a]) * Rate / (b - a);
    }

    private static double Correlation(double[] yaw, double[] course, bool[] moving, int lag)
    {
        double x = 0, y = 0, xx = 0, yy = 0, xy = 0;
        var count = 0;
        for (var i = Math.Max(0, -lag); i < Math.Min(yaw.Length, yaw.Length - lag); i++)
        {
            if (!moving[i] || !moving[i + lag]) continue;
            var a = yaw[i];
            var b = course[i + lag];
            x += a; y += b; xx += a * a; yy += b * b; xy += a * b;
            count++;
        }
        if (count < MinimumCommonCells) return double.NaN;
        var vx = xx - x * x / count;
        var vy = yy - y * y / count;
        if (vx <= 0 || vy <= 0) return double.NaN;
        return Math.Clamp((xy - x * y / count) / Math.Sqrt(vx * vy), -1, 1);
    }
}
