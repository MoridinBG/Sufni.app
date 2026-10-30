namespace Sufni.Telemetry;

/// <summary>Recording header metadata; start times are UTC.</summary>
public abstract record SstFileInspection();

public sealed record ValidSstFileInspection(
    byte Version,
    DateTime StartTime,
    TimeSpan Duration,
    ushort TelemetrySampleRate,
    bool HasUnknown,
    string? MalformedMessage = null) : SstFileInspection();

public sealed record MalformedSstFileInspection(
    byte? Version,
    DateTime? StartTime,
    TimeSpan? Duration,
    ushort? TelemetrySampleRate,
    string Message) : SstFileInspection();