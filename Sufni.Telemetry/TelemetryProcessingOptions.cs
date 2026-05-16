namespace Sufni.Telemetry;

public sealed record TelemetryProcessingOptions(int VelocityFilterWindowMilliseconds)
{
    public const int MinVelocityFilterWindowMilliseconds = 0;
    public const int DefaultVelocityFilterWindowMilliseconds = 50;
    public const int MaxVelocityFilterWindowMilliseconds = 1000;

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
}
