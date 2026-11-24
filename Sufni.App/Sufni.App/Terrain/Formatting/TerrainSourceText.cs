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
        RoughnessSource.LowersOnly => "Terrain · Lowers sensor",
        _ => "Terrain"
    };

    public static string? Description(TerrainSourceCoverage? coverage)
    {
        if (coverage is null) return null;
        var lines = new List<string>();
        if (coverage.Source is not null)
            lines.Add($"Both sensors {coverage.JointShare:P0} · Frame only {coverage.FrameOnlyShare:P0} · Lowers only {coverage.LowersOnlyShare:P0}");
        if (coverage.LowersClipped) lines.Add("Lowers reached full scale.");
        if (coverage.FrameMissing) lines.Add("Frame data absent in part or all of the ride.");
        if (coverage.LowersMissing) lines.Add("Lowers data absent in part or all of the ride.");
        if (coverage.FrameUncalibrated) lines.Add("Frame scale has no calibration.");
        if (coverage.LowersUncalibrated) lines.Add("Lowers scale has no calibration.");
        foreach (var sensor in coverage.Silent) lines.Add($"{SensorName(sensor)} sensor ignored: no movement recorded.");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string SensorName(TerrainSensor sensor) => sensor switch
    {
        TerrainSensor.Front => "Front suspension",
        TerrainSensor.Rear => "Rear suspension",
        TerrainSensor.Frame => "Frame",
        _ => "Lowers"
    };
}
