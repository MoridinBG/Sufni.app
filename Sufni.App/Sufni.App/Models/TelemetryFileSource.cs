namespace Sufni.App.Models;

/// <summary>
/// Raw bytes and display name read from an importable telemetry file.
/// The value represents the canonical file content before it is persisted as
/// a recorded-session source.
/// </summary>
public sealed record TelemetryFileSource(string FileName, byte[] SstBytes);
