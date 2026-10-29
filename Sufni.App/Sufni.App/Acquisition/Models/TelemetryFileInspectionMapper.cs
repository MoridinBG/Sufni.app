using System;
using Sufni.Telemetry;

namespace Sufni.App.Acquisition.Models;

internal static class TelemetryFileInspectionMapper
{
    public static TelemetryFileInspectionState Map(SstFileInspection inspection, DateTime fallbackStartTime)
    {
        return inspection switch
        {
            ValidSstFileInspection valid => new TelemetryFileInspectionState(
                ShouldBeImported: false,
                StartTime: valid.StartTime,
                Duration: valid.Duration.ToString(@"hh\:mm\:ss"),
                MalformedMessage: valid.MalformedMessage,
                CanImport: true),
            MalformedSstFileInspection malformed => new TelemetryFileInspectionState(
                ShouldBeImported: false,
                StartTime: malformed.StartTime ?? fallbackStartTime,
                Duration: malformed.Duration?.ToString(@"hh\:mm\:ss") ?? "unknown",
                MalformedMessage: malformed.Message,
                CanImport: false),
            _ => throw new ArgumentOutOfRangeException(nameof(inspection), inspection, "Unknown SST inspection result.")
        };
    }
}

internal sealed record TelemetryFileInspectionState(
    bool? ShouldBeImported,
    DateTime StartTime,
    string Duration,
    string? MalformedMessage,
    bool CanImport);
