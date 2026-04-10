namespace Sufni.Telemetry;

public abstract record SstFileInspection(bool HasUnknown);

public sealed record ValidSstFileInspection(
    byte Version,
    DateTime StartTime,
    TimeSpan Duration,
    ushort TelemetrySampleRate,
    bool HasUnknown) : SstFileInspection(HasUnknown);

public sealed record MalformedSstFileInspection(
    byte? Version,
    DateTime? StartTime,
    TimeSpan? Duration,
    ushort? TelemetrySampleRate,
    bool HasUnknown,
    string Message) : SstFileInspection(HasUnknown);