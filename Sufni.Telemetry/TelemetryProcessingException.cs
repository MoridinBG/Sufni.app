namespace Sufni.Telemetry;

public enum TelemetryProcessingFailureReason
{
    NoTravelData,
    RecordCountMismatch,
    VelocitySamplesMissing
}

/// <summary>Identifies a recording or derived trace that cannot be processed.</summary>
public sealed class TelemetryProcessingException(TelemetryProcessingFailureReason reason) : Exception(reason switch
{
    TelemetryProcessingFailureReason.NoTravelData => "No calibrated travel channel contains usable samples.",
    TelemetryProcessingFailureReason.RecordCountMismatch => "Front and rear record counts are not equal.",
    TelemetryProcessingFailureReason.VelocitySamplesMissing => "Velocity samples are required.",
    _ => throw new ArgumentOutOfRangeException(nameof(reason))
})
{
    public TelemetryProcessingFailureReason Reason { get; } = reason;
}
