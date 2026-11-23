using System;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public readonly record struct TerrainClassFilter
{
    private readonly int roughness;
    private readonly int steepness;

    private TerrainClassFilter(int roughness, int steepness) => (this.roughness, this.steepness) = (roughness, steepness);

    public bool IsEmpty => roughness == 0 && steepness == 0;
    public bool Contains(RoughnessClass value) => (roughness & (1 << (int)value)) != 0;
    public bool Contains(SteepnessClass value) => (steepness & (1 << (int)value)) != 0;
    public TerrainClassFilter Toggle(RoughnessClass value) => new(roughness ^ (1 << (int)value), steepness);
    public TerrainClassFilter Toggle(SteepnessClass value) => new(roughness, steepness ^ (1 << (int)value));

    public bool Matches(TerrainWindow window) => window.Moving &&
        (roughness == 0 || window.Roughness is { } r && Contains(r)) &&
        (steepness == 0 || window.Steepness is { } s && Contains(s));
}

public closed record AnalysisScope
{
    private AnalysisScope() { }
    public sealed record Whole : AnalysisScope;
    public sealed record SegmentComparison(Guid MatchedSessionId) : AnalysisScope;
    public sealed record ClassFilter : AnalysisScope
    {
        public TerrainClassFilter Filter { get; }
        internal ClassFilter(TerrainClassFilter filter)
        {
            if (filter.IsEmpty) throw new ArgumentException("Select at least one terrain class.", nameof(filter));
            Filter = filter;
        }
    }

    public static AnalysisScope For(TerrainClassFilter filter) => filter.IsEmpty ? new Whole() : new ClassFilter(filter);
}
