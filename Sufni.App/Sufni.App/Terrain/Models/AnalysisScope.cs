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

    /// <summary>An empty axis admits every class on that axis, including no class.</summary>
    public bool Matches(ClassifiedWindow window) =>
        (roughness == 0 || window.Roughness is { } r && Contains(r)) &&
        (grade == 0 || window.Window.Band is { } b && Contains(b));
}

public enum TerrainPreset { Descents, Climbs, All }

/// <summary>
/// The moving ground that analysis uses: a preset, narrowed by legend toggles. Descents is every non-climb window,
/// including windows without grade. A toggle outside the preset is never set.
/// </summary>
public sealed record TerrainScope(TerrainPreset Preset, TerrainClassFilter Filter = default)
{
    public static TerrainScope Default(TerrainProfile profile) =>
        new(profile.GradeState == TerrainGradeState.Available ? TerrainPreset.Descents : TerrainPreset.All);

    public bool Admits(GradeBand band) => Admits(band.IsClimb());
    public bool Admits(RoughnessClass value) => Admits(value.IsClimb());
    public bool Matches(ClassifiedWindow window) => window.Window.Moving && Admits(window.Window.Climbing) && Filter.Matches(window);
    public TerrainScope Toggle(GradeBand band) => Admits(band) ? this with { Filter = Filter.Toggle(band) } : this;
    public TerrainScope Toggle(RoughnessClass value) => Admits(value) ? this with { Filter = Filter.Toggle(value) } : this;

    private bool Admits(bool climb) => Preset switch
    {
        TerrainPreset.Descents => !climb,
        TerrainPreset.Climbs => climb,
        _ => true
    };
}

public closed record AnalysisScope
{
    private AnalysisScope() { }

    /// <summary>The terrain scope that the loaded profile implies, or the analysis range without a profile.</summary>
    public sealed record Default : AnalysisScope;
    public sealed record Terrain(TerrainScope Value) : AnalysisScope;
    public sealed record SegmentComparison(Guid MatchedSessionId) : AnalysisScope;
}
