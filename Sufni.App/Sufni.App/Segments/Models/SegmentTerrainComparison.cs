using System;
using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Models;

/// <summary>The source ride of a same-trail request, with the scale catalogue that classes the matched runs.</summary>
public sealed record SegmentTerrainSource(Guid SessionId, TerrainProfileKey Key, ClassifiedProfile Classified,
    TerrainBaselineIdentity Baseline, TerrainScaleCatalog Catalog);

public enum SegmentTerrainUnavailable { NoSourceProfile, NoMatchProfile, NoCommonSensor, Unaligned }

public closed record SegmentTerrainComparison
{
    public sealed record Compared(ImmutableArray<TerrainComparisonRow> Rows, int? AverageRunCount = null) : SegmentTerrainComparison;
    public sealed record Unavailable(SegmentTerrainUnavailable Reason) : SegmentTerrainComparison;
    public sealed record Failed : SegmentTerrainComparison;
}
