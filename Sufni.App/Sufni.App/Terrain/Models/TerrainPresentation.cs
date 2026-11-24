using System.Collections.Immutable;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public enum MapTrackColoring { Plain, Roughness, Steepness }

public enum TerrainLevel { Low, Middle, High }

public readonly record struct TerrainClassRun(double StartSeconds, double EndSeconds, TerrainLevel Level);

public sealed record TerrainSignalSection(ImmutableArray<double> Seconds, ImmutableArray<double> Values);

public sealed record TerrainSourceCoverage(double JointShare, double FrameOnlyShare, double LowersOnlyShare,
    bool LowersClipped, bool FrameMissing, bool LowersMissing, bool FrameUncalibrated, bool LowersUncalibrated,
    ImmutableArray<TerrainSensor> Silent)
{
    public RoughnessSource? Source => (JointShare + FrameOnlyShare > 0, JointShare + LowersOnlyShare > 0) switch
    {
        (true, true) => RoughnessSource.Both,
        (true, false) => RoughnessSource.FrameOnly,
        (false, true) => RoughnessSource.LowersOnly,
        _ => null
    };
}

public sealed record TerrainPresentation(ImmutableArray<TerrainClassRun> RoughnessRuns,
    ImmutableArray<TerrainClassRun> SteepnessRuns, ImmutableArray<TerrainSignalSection> Frame,
    ImmutableArray<TerrainSignalSection> Lowers, ImmutableArray<TerrainSignalSection> Grade, TerrainSourceCoverage Coverage);
