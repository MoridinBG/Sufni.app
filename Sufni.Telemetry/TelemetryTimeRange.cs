namespace Sufni.Telemetry;

public readonly record struct TelemetryTimeRange
{
    private enum RangeValidationState
    {
        Normalized,
    }

    public const double MinimumDurationSeconds = 0.1;

    public double StartSeconds { get; }
    public double EndSeconds { get; }
    public double DurationSeconds => EndSeconds - StartSeconds;

    public TelemetryTimeRange(double startSeconds, double endSeconds)
    {
        if (!TryNormalize(startSeconds, endSeconds, out var start, out var end))
        {
            throw new ArgumentException("Range boundaries must be finite.");
        }

        if (end - start < MinimumDurationSeconds)
        {
            throw new ArgumentException($"Telemetry range must be at least {MinimumDurationSeconds} seconds long.");
        }

        StartSeconds = start;
        EndSeconds = end;
    }

    private TelemetryTimeRange(double startSeconds, double endSeconds, RangeValidationState _)
    {
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
    }

    public static bool TryCreate(double startSeconds, double endSeconds, out TelemetryTimeRange range)
    {
        range = default;
        if (!TryNormalize(startSeconds, endSeconds, out var start, out var end) ||
            end - start < MinimumDurationSeconds)
        {
            return false;
        }

        range = new TelemetryTimeRange(start, end, RangeValidationState.Normalized);
        return true;
    }

    public static bool TryCreateClamped(
        double startSeconds,
        double endSeconds,
        double durationSeconds,
        out TelemetryTimeRange range)
    {
        range = default;
        if (!HasValidDuration(durationSeconds) ||
            !TryNormalize(startSeconds, endSeconds, out var start, out var end))
        {
            return false;
        }

        start = Math.Clamp(start, 0, durationSeconds);
        end = Math.Clamp(end, 0, durationSeconds);
        if (end - start < MinimumDurationSeconds)
        {
            return false;
        }

        range = new TelemetryTimeRange(start, end, RangeValidationState.Normalized);
        return true;
    }

    public static bool TryClampBoundary(
        double boundarySeconds,
        double durationSeconds,
        out double clampedSeconds)
    {
        clampedSeconds = default;
        if (!IsFinite(boundarySeconds) || !HasValidDuration(durationSeconds))
        {
            return false;
        }

        clampedSeconds = Math.Clamp(boundarySeconds, 0, durationSeconds);
        return true;
    }

    private static bool TryNormalize(double startSeconds, double endSeconds, out double start, out double end)
    {
        start = default;
        end = default;
        if (!IsFinite(startSeconds) || !IsFinite(endSeconds))
        {
            return false;
        }

        start = Math.Min(startSeconds, endSeconds);
        end = Math.Max(startSeconds, endSeconds);
        return true;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool HasValidDuration(double durationSeconds)
    {
        return IsFinite(durationSeconds) && durationSeconds > 0;
    }
}
