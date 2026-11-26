using System;
using System.Collections.Immutable;
using Avalonia.Media;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

// Preferences store the numeric value, so a saved Steepness choice reads as Grade.
public enum MapTrackColoring { Plain, Roughness, Grade }

/// <summary>Adjacent moving windows of one class on one axis.</summary>
public readonly record struct TerrainRun<TClass>(double StartSeconds, double EndSeconds, TClass Class) where TClass : struct, Enum;

/// <summary>
/// Adjacent moving windows of one class and one rounded value on one axis. A null value takes the plain colour of the class.
/// </summary>
public readonly record struct TerrainShade<TClass>(double StartSeconds, double EndSeconds, TClass Class, double? Value)
    where TClass : struct, Enum;

/// <summary>A run with its theme colour, for surfaces that do not know terrain classes.</summary>
public readonly record struct TerrainColourRun(double StartSeconds, double EndSeconds, Color Colour);

public sealed record TerrainSignalSection(ImmutableArray<double> Seconds, ImmutableArray<double> Values);

/// <summary>
/// How the classified descent windows read the ground. <see cref="SourceShares"/> holds the share of those windows per
/// <see cref="GroundSensors"/> value. A clipped flag marks a clipped reading that stayed out of a class value; a missing
/// flag marks a classified descent window without that sensor's reading. <see cref="ClimbReading"/> marks a climb window
/// with a frame reading. <see cref="LabelledShare"/> is the share of the moving windows with a class whose class is a label.
/// </summary>
public sealed record TerrainSourceCoverage(ImmutableArray<double> SourceShares, bool ForkClipped, bool RearAxleClipped,
    bool FrameMissing, bool ForkMissing, bool RearAxleMissing, bool ClimbReading, ImmutableArray<TerrainSensor> Silent,
    double LabelledShare)
{
    /// <summary>The source of the largest share; equal shares give the first source.</summary>
    public GroundSensors? Source
    {
        get
        {
            GroundSensors? source = null;
            var largest = 0d;
            foreach (var sensor in Enum.GetValues<GroundSensors>())
                if (SourceShares[(int)sensor] > largest) (source, largest) = (sensor, SourceShares[(int)sensor]);
            return source;
        }
    }
}

public sealed record TerrainPresentation(ImmutableArray<TerrainRun<RoughnessClass>> RoughnessRuns,
    ImmutableArray<TerrainRun<GradeBand>> GradeRuns, ImmutableArray<TerrainSignalSection> Frame,
    ImmutableArray<TerrainSignalSection> Fork, ImmutableArray<TerrainSignalSection> Grade, TerrainSourceCoverage Coverage)
{
    /// <summary>The class value of each window in steps of 0.25 class.</summary>
    public ImmutableArray<TerrainShade<RoughnessClass>> RoughnessShades { get; init; } = [];
    /// <summary>The baseline grade of each window in steps of 1°.</summary>
    public ImmutableArray<TerrainShade<GradeBand>> GradeShades { get; init; } = [];
    /// <summary>
    /// The terrain labels in time order. Where labels overlap, the part that the first label holds is cut from the later one.
    /// </summary>
    public ImmutableArray<TerrainRun<RoughnessClass>> LabelRuns { get; init; } = [];
    /// <summary>Speed-removed frame levels at the descent class boundaries; empty without a ready frame median.</summary>
    public ImmutableArray<double> FrameEdges { get; init; } = [];
    public ImmutableArray<double> ForkEdges { get; init; } = [];
    public ImmutableArray<double> RearAxleEdges { get; init; } = [];
    public ImmutableArray<TerrainSignalSection> RearAxle { get; init; } = [];
    /// <summary>Rear bob in mm, only in windows where pedalling is detected.</summary>
    public ImmutableArray<TerrainSignalSection> PedalBob { get; init; } = [];
}
