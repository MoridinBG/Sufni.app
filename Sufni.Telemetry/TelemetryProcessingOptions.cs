namespace Sufni.Telemetry;

public sealed record TelemetryProcessingOptions(int VelocityFilterWindowMilliseconds)
{
    public const int MinVelocityFilterWindowMilliseconds = 0;
    public const int DefaultVelocityFilterWindowMilliseconds = 25;
    public const int MaxVelocityFilterWindowMilliseconds = 1000;

    // Smallest Savitzky-Golay window the velocity filter will use, regardless of
    // how short the requested millisecond window works out to in samples.
    public const int MinVelocityFilterWindowSamples = 5;

    public TelemetryProcessingOptions()
        : this(DefaultVelocityFilterWindowMilliseconds)
    {
    }

    public static TelemetryProcessingOptions Default { get; } = new();

    public int ClampedVelocityFilterWindowMilliseconds =>
        Math.Clamp(
            VelocityFilterWindowMilliseconds,
            MinVelocityFilterWindowMilliseconds,
            MaxVelocityFilterWindowMilliseconds);

    public bool UsesVelocityFilter => ClampedVelocityFilterWindowMilliseconds > 0;

    public double VelocityFilterWindowSeconds => ClampedVelocityFilterWindowMilliseconds / 1000.0;

    // Effective velocity-filter width in samples for a recording at the given
    // sample rate: the millisecond window converted to whole samples, forced odd
    // (the filter is symmetric), and floored at the minimum window. Returns 0
    // when the filter is off or the sample rate is unknown. Does not apply the
    // recording-length clamp, which only bites on very short recordings.
    public int VelocityFilterWindowSamples(int sampleRate)
    {
        if (!UsesVelocityFilter || sampleRate <= 0)
        {
            return 0;
        }

        var samples = (int)Math.Round(sampleRate * VelocityFilterWindowSeconds);
        if (samples % 2 == 0)
        {
            samples++;
        }

        return Math.Max(samples, MinVelocityFilterWindowSamples);
    }
}
