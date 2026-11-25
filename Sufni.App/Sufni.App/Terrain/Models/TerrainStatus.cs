using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public sealed record TerrainProfileKey(SessionContentToken Content, int AlgorithmVersion)
{
    public static TerrainProfileKey From(SessionContentToken content) => new(content, TerrainProfileCalculator.AlgorithmVersion);
}

/// <summary>The inputs of a stored ride class summary.</summary>
public sealed record TerrainClassKey(TerrainProfileKey Profile, TerrainScaleKey Scale, int ClassifierVersion);

public enum TerrainOutcome { Computed = 0, NoTrack = 1, NoMovement = 2, ContentMissing = 3, Failed = 4 }

public enum TerrainAvailabilityReason { NoTelemetry, NoTrack, NoMovement, ContentMissing }

public closed record TerrainStatus
{
    private TerrainStatus() { }

    public sealed record Pending : TerrainStatus;
    public sealed record Current(TerrainProfileKey Key, TerrainSummary Summary) : TerrainStatus;
    public sealed record Unavailable(TerrainAvailabilityReason Reason) : TerrainStatus;
    public sealed record Failed : TerrainStatus;
}

/// <summary>
/// The ride class summary of a session on the scale of its setup. <see cref="Pending"/> asks for class work;
/// <see cref="Unavailable"/> covers a profile that is not current and a setup scale without a ready bin.
/// </summary>
public closed record TerrainClassState
{
    private TerrainClassState() { }

    public sealed record Classified(TerrainClassSummary Summary) : TerrainClassState;
    public sealed record Pending : TerrainClassState;
    public sealed record Unavailable : TerrainClassState;
}
