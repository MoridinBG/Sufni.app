using System;
using System.Collections.Immutable;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

/// <summary>
/// The median level of one sensor over the rides of a setup, once it has enough of them. <see cref="Anchored"/> is the
/// median that the labelled readings of the setup set.
/// </summary>
public closed record SensorScale
{
    private SensorScale() { }
    public sealed record Ready(int MedianBin) : SensorScale;
    public sealed record Anchored(int MedianBin) : SensorScale;
    public sealed record NotEnoughData(int Rides, double Metres) : SensorScale;
}

/// <summary>The labelled metres of a setup for each sensor and climb class.</summary>
public sealed record TerrainLabelCoverage(double FrameImu, double ForkImu, double RearAxleImu, double SmoothClimb, double TechnicalClimb)
{
    public static TerrainLabelCoverage None { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>The terrain scale of one setup, derived from the measurement and label summaries of its current profiles.</summary>
public sealed record SetupTerrainScale(SensorScale FrameImu, SensorScale ForkImu, SensorScale RearAxleImu, SensorScale FrameClimb,
    TerrainLabelCoverage Labels)
{
    public TerrainScaleKey Key => new(Bin(FrameImu), Bin(ForkImu), Bin(RearAxleImu), Bin(FrameClimb));

    private static int? Bin(SensorScale scale) => scale switch
    {
        SensorScale.Ready ready => ready.MedianBin,
        SensorScale.Anchored anchored => anchored.MedianBin,
        _ => null
    };
}

/// <summary>The scale key of each setup. A setup without an entry has no ready bin.</summary>
public sealed record TerrainScaleCatalog(ImmutableDictionary<Guid, TerrainScaleKey> Keys)
{
    public static TerrainScaleCatalog Empty { get; } = new(ImmutableDictionary<Guid, TerrainScaleKey>.Empty);

    public TerrainScaleKey ScaleOf(Guid? setupId) =>
        setupId is { } id && Keys.TryGetValue(id, out var key) ? key : TerrainScaleKey.None;

    public bool Equals(TerrainScaleCatalog? other) => other is not null && Keys.Count == other.Keys.Count &&
        Keys.All(entry => other.Keys.TryGetValue(entry.Key, out var key) && key == entry.Value);

    public override int GetHashCode() => Keys.Aggregate(0, (hash, entry) => hash ^ HashCode.Combine(entry.Key, entry.Value));
}
