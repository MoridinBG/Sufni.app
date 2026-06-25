namespace Sufni.Telemetry;

public sealed class SuspensionTimeSeriesSampler
{
    private readonly ProcessedSuspensionSegment[] segments;
    private readonly int sampleRate;

    public SuspensionTimeSeriesSampler(ProcessedSuspensionSegment[] segments, int sampleRate)
    {
        this.segments = segments.OrderBy(segment => segment.StartSeconds).ToArray();
        this.sampleRate = sampleRate;
    }

    public bool TrySampleTravel(double seconds, out double travel) =>
        TrySample(seconds, segment => segment.Travel, out travel);

    public bool TrySampleVelocity(double seconds, out double velocity) =>
        TrySample(seconds, segment => segment.Velocity, out velocity);

    private bool TrySample(double seconds, Func<ProcessedSuspensionSegment, double[]> valuesSelector, out double value)
    {
        value = 0;
        if (sampleRate <= 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            var values = valuesSelector(segment);
            if (values.Length == 0)
            {
                continue;
            }

            var segmentEndSeconds = segment.StartSeconds + (values.Length - 1) / (double)sampleRate;
            if (seconds < segment.StartSeconds || seconds > segmentEndSeconds)
            {
                continue;
            }

            var position = (seconds - segment.StartSeconds) * sampleRate;
            var lower = (int)Math.Floor(position);
            var upper = Math.Min(lower + 1, values.Length - 1);
            if (lower < 0 || lower >= values.Length)
            {
                return false;
            }

            var fraction = position - lower;
            value = values[lower] + (values[upper] - values[lower]) * fraction;
            return true;
        }

        return false;
    }
}
