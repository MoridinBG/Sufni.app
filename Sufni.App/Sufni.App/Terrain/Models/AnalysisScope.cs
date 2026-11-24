using System;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public readonly record struct TerrainClassFilter
{
    private readonly int roughness;
    private readonly int grade;

    private TerrainClassFilter(int roughness, int grade) => (this.roughness, this.grade) = (roughness, grade);

    public bool IsEmpty => roughness == 0 && grade == 0;
    public bool Contains(RoughnessClass value) => (roughness & (1 << (int)value)) != 0;
    public bool Contains(GradeBand value) => (grade & (1 << (int)value)) != 0;
    public TerrainClassFilter Toggle(RoughnessClass value) => new(roughness ^ (1 << (int)value), grade);
    public TerrainClassFilter Toggle(GradeBand value) => new(roughness, grade ^ (1 << (int)value));

    public bool Matches(TerrainWindow window) => window.Moving &&
        (roughness == 0 || window.Roughness is { } r && Contains(r)) &&
        (grade == 0 || window.Band is { } b && Contains(b));
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
