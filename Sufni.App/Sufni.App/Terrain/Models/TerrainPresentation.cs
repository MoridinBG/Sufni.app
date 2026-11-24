using System;
using System.Collections.Immutable;
using Avalonia.Media;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public enum MapTrackColoring { Plain, Roughness, Steepness }

/// <summary>Adjacent moving windows of one class on one axis.</summary>
public readonly record struct TerrainRun<TClass>(double StartSeconds, double EndSeconds, TClass Class) where TClass : struct, Enum;

/// <summary>A run with its theme colour, for surfaces that do not know terrain classes.</summary>
public readonly record struct TerrainColourRun(double StartSeconds, double EndSeconds, Color Colour);

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

public sealed record TerrainPresentation(ImmutableArray<TerrainRun<RoughnessClass>> RoughnessRuns,
    ImmutableArray<TerrainRun<GradeBand>> GradeRuns, ImmutableArray<TerrainSignalSection> Frame,
    ImmutableArray<TerrainSignalSection> Lowers, ImmutableArray<TerrainSignalSection> Grade, TerrainSourceCoverage Coverage);
