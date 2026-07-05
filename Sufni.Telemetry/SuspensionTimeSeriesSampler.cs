namespace Sufni.Telemetry;

public sealed class SuspensionTimeSeriesSampler
{
    private readonly ProcessedSuspensionSegment[] segments;
    private readonly double[] values;
    private readonly int sampleRate;

    public SuspensionTimeSeriesSampler(ProcessedSuspensionSegment[] segments, double[] values, int sampleRate)
    {
        this.segments = segments.OrderBy(segment => segment.StartSeconds).ToArray();
        this.values = values;
        this.sampleRate = sampleRate;
    }

    public bool TrySampleTravel(double seconds, out double travel) =>
        TrySample(seconds, out travel);

    public bool TrySampleVelocity(double seconds, out double velocity) =>
        TrySample(seconds, out velocity);

    private bool TrySample(double seconds, out double value)
    {
        value = 0;
        if (sampleRate <= 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.SampleCount <= 0)
            {
                continue;
            }

            var segmentEndSeconds = segment.StartSeconds + (segment.SampleCount - 1) / (double)sampleRate;
            if (seconds < segment.StartSeconds || seconds > segmentEndSeconds)
            {
                continue;
            }

            var position = (seconds - segment.StartSeconds) * sampleRate;
            var lower = (int)Math.Floor(position);
            var upper = Math.Min(lower + 1, segment.SampleCount - 1);
            if (lower < 0 || lower >= segment.SampleCount)
            {
                return false;
            }

            var lowerDenseIndex = segment.FirstDenseIndex + lower;
            var upperDenseIndex = segment.FirstDenseIndex + upper;
            if (lowerDenseIndex < 0 ||
                lowerDenseIndex >= values.Length ||
                upperDenseIndex < 0 ||
                upperDenseIndex >= values.Length)
            {
                return false;
            }

            var fraction = position - lower;
            value = values[lowerDenseIndex] + (values[upperDenseIndex] - values[lowerDenseIndex]) * fraction;
            return true;
        }

        return false;
    }
}
