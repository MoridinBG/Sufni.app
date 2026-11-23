using System.Collections.Immutable;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public enum TerrainLevel { Low, Middle, High }

public readonly record struct TerrainClassRun(double StartSeconds, double EndSeconds, TerrainLevel Level);

public readonly record struct TerrainSignalSample(double StartSeconds, double EndSeconds, double? Frame,
    double? Lowers, double? GradeDegrees)
{
    public double MidSeconds => (StartSeconds + EndSeconds) / 2;
}

public sealed record TerrainSourceCoverage(double JointShare, double FrameOnlyShare, double LowersOnlyShare,
    bool LowersClipped, bool FrameMissing, bool LowersMissing, bool FrameUncalibrated, bool LowersUncalibrated)
{
    public RoughnessSource? Source => (JointShare + FrameOnlyShare > 0, JointShare + LowersOnlyShare > 0) switch
    {
        (true, true) => RoughnessSource.Both,
        (true, false) => RoughnessSource.FrameOnly,
        (false, true) => RoughnessSource.LowersOnly,
        _ => null
    };

    public string? Label => Source switch
    {
        RoughnessSource.Both => "Both sensors",
        RoughnessSource.FrameOnly => "Frame sensor",
        RoughnessSource.LowersOnly => "Lowers sensor",
        _ => null
    };

    public string? Description
    {
        get
        {
            if (Source is null) return null;
            var lines = new List<string> { $"Both sensors {JointShare:P0} · Frame only {FrameOnlyShare:P0} · Lowers only {LowersOnlyShare:P0}" };
            if (LowersClipped) lines.Add("Lowers reached full scale.");
            if (FrameMissing) lines.Add("Frame data absent in part or all of the ride.");
            if (LowersMissing) lines.Add("Lowers data absent in part or all of the ride.");
            if (FrameUncalibrated) lines.Add("Frame scale has no calibration.");
            if (LowersUncalibrated) lines.Add("Lowers scale has no calibration.");
            return string.Join("\n", lines);
        }
    }
}

public sealed record TerrainPresentation(ImmutableArray<TerrainClassRun> RoughnessRuns,
    ImmutableArray<TerrainClassRun> SteepnessRuns, ImmutableArray<TerrainSignalSample> Signals,
    TerrainSourceCoverage Coverage);
