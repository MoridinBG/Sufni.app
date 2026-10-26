namespace Sufni.App.Video.Models;

/// <summary>
/// The session start (unix seconds) and the telemetry duration that map the
/// telemetry cursor to video time.
/// </summary>
public sealed record VideoSessionTiming(long? Timestamp, double? DurationSeconds);
