using System.Collections.Generic;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class TerrainSourceText
{
    public static string StripTitle(TerrainSourceCoverage? coverage) => coverage?.Source switch
    {
        RoughnessSource.Both => "Terrain · Both sensors",
        RoughnessSource.FrameOnly => "Terrain · Frame sensor",
        RoughnessSource.ForkOnly => "Terrain · Fork sensor",
        _ => "Terrain"
    };

    public static string? Description(TerrainSourceCoverage? coverage)
    {
        if (coverage is null) return null;
        var lines = new List<string>();
        if (coverage.Source is not null)
            lines.Add($"Both sensors {coverage.JointShare:P0} · Frame only {coverage.FrameOnlyShare:P0} · Fork only {coverage.ForkOnlyShare:P0}");
        if (coverage.ForkClipped) lines.Add("Fork reached full scale.");
        if (coverage.FrameMissing) lines.Add("Frame data absent in part or all of the ride.");
        if (coverage.ForkMissing) lines.Add("Fork data absent in part or all of the ride.");
        if (coverage.FrameUncalibrated) lines.Add("Frame scale has no calibration.");
        if (coverage.ForkUncalibrated) lines.Add("Fork scale has no calibration.");
        foreach (var sensor in coverage.Silent) lines.Add($"{SensorName(sensor)} sensor ignored: no movement recorded.");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string SensorName(TerrainSensor sensor) => sensor switch
    {
        TerrainSensor.ForkTravel => "Front suspension",
        TerrainSensor.ShockTravel => "Rear suspension",
        TerrainSensor.FrameImu => "Frame",
        _ => "Fork"
    };
}
