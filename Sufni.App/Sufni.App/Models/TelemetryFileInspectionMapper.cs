using System;
using Sufni.Telemetry;

namespace Sufni.App.Models;

internal static class TelemetryFileInspectionMapper
{
    public static TelemetryFileInspectionState Map(SstFileInspection inspection, DateTime fallbackStartTime)
    {
        return inspection switch
        {
            ValidSstFileInspection valid => new TelemetryFileInspectionState(
                ShouldBeImported: false,
                Version: valid.Version,
                StartTime: valid.StartTime,
                Duration: valid.Duration.ToString(@"hh\:mm\:ss"),
                MalformedMessage: valid.MalformedMessage,
                CanImport: true,
                HasUnknown: valid.HasUnknown),
            MalformedSstFileInspection malformed => new TelemetryFileInspectionState(
                ShouldBeImported: false,
                Version: malformed.Version ?? 0,
                StartTime: malformed.StartTime ?? fallbackStartTime,
                Duration: malformed.Duration?.ToString(@"hh\:mm\:ss") ?? "unknown",
                MalformedMessage: malformed.Message,
                CanImport: false,
                HasUnknown: false),
            _ => throw new ArgumentOutOfRangeException(nameof(inspection), inspection, "Unknown SST inspection result.")
        };
    }
}

internal sealed record TelemetryFileInspectionState(
    bool? ShouldBeImported,
    byte Version,
    DateTime StartTime,
    string Duration,
    string? MalformedMessage,
    bool CanImport,
    bool HasUnknown);
