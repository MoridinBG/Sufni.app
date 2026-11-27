using System;
using System.Collections.Immutable;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

/// <summary>
/// The median level of one sensor over the rides of a setup, once it has enough of them. <see cref="Anchored"/> is the
/// median that the labelled stretches of the setup fit best.
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

/// <summary>
/// The terrain scale of one setup, derived from the measurement and label summaries of its current profiles.
/// <see cref="Readings"/> holds how each labelled stretch reads on the scale of everything else: the other labels, or
/// the rides when the other labels do not anchor a sensor. A label that sets the scale alone cannot test itself.
/// </summary>
public sealed record SetupTerrainScale(SensorScale FrameImu, SensorScale ForkImu, SensorScale RearAxleImu, SensorScale FrameClimb,
    TerrainLabelCoverage Labels)
{
    public ImmutableDictionary<(Guid SessionId, TerrainLabelRange Label), LabelReading> Readings { get; init; } =
        ImmutableDictionary<(Guid SessionId, TerrainLabelRange Label), LabelReading>.Empty;

    public TerrainScaleKey Key => new(Bin(FrameImu), Bin(ForkImu), Bin(RearAxleImu), Bin(FrameClimb));

    /// <summary>The reading of a label of a session, null without a labelled stretch on a sensor with a median.</summary>
    public LabelReading? ReadingOf(Guid sessionId, TerrainLabelRange label) => Readings.GetValueOrDefault((sessionId, label));

    public bool Equals(SetupTerrainScale? other) => other is not null && FrameImu == other.FrameImu && ForkImu == other.ForkImu &&
        RearAxleImu == other.RearAxleImu && FrameClimb == other.FrameClimb && Labels == other.Labels &&
        Readings.Count == other.Readings.Count &&
        Readings.All(entry => other.Readings.TryGetValue(entry.Key, out var reading) && reading == entry.Value);

    public override int GetHashCode() => HashCode.Combine(FrameImu, ForkImu, RearAxleImu, FrameClimb, Labels, Readings.Count);

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
