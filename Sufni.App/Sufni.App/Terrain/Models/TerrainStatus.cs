using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public sealed record TerrainProfileKey(SessionContentToken Content, int AlgorithmVersion, int CalibrationVersion)
{
    public static TerrainProfileKey From(SessionContentToken content) =>
        new(content, TerrainProfileCalculator.AlgorithmVersion, TerrainCalibration.Version);
}

public enum TerrainOutcome { Computed, NoTrack, NoMovement, ContentMissing, Failed }

public enum TerrainAvailabilityReason { NoTelemetry, NoTrack, NoMovement, ContentMissing }

public closed record TerrainStatus
{
    private TerrainStatus() { }

    public sealed record Pending : TerrainStatus;
    public sealed record Current(TerrainProfileKey Key, TerrainSummary Summary) : TerrainStatus;
    public sealed record Unavailable(TerrainAvailabilityReason Reason) : TerrainStatus;
    public sealed record Failed : TerrainStatus;
}
