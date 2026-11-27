using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public enum TerrainLabelKind { Roughness = 0, Steepness = 1 }

/// <summary>What a terrain label says about its range: a roughness class or a grade band.</summary>
public closed record TerrainLabelClass
{
    private TerrainLabelClass() { }

    public sealed record Roughness(RoughnessClass Class) : TerrainLabelClass;
    public sealed record Steepness(GradeBand Band) : TerrainLabelClass;

    public TerrainLabelKind Kind => this is Steepness ? TerrainLabelKind.Steepness : TerrainLabelKind.Roughness;
}
