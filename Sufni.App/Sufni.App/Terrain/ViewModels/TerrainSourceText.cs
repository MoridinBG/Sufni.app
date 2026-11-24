using System.Collections.Generic;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

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
        if (coverage?.Source is null) return null;
        var lines = new List<string>
        {
            $"Both sensors {coverage.JointShare:P0} · Frame only {coverage.FrameOnlyShare:P0} · Lowers only {coverage.LowersOnlyShare:P0}"
        };
        if (coverage.LowersClipped) lines.Add("Lowers reached full scale.");
        if (coverage.FrameMissing) lines.Add("Frame data absent in part or all of the ride.");
        if (coverage.LowersMissing) lines.Add("Lowers data absent in part or all of the ride.");
        if (coverage.FrameUncalibrated) lines.Add("Frame scale has no calibration.");
        if (coverage.LowersUncalibrated) lines.Add("Lowers scale has no calibration.");
        return string.Join("\n", lines);
    }
}
