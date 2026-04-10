namespace Sufni.Telemetry;

public abstract record SstFileInspection();

public sealed record ValidSstFileInspection(
    byte Version,
    DateTime StartTime,
    TimeSpan Duration,
    ushort TelemetrySampleRate,
    bool HasUnknown) : SstFileInspection();

public sealed record MalformedSstFileInspection(
    byte? Version,
    DateTime? StartTime,
    TimeSpan? Duration,
    ushort? TelemetrySampleRate,
    string Message) : SstFileInspection();