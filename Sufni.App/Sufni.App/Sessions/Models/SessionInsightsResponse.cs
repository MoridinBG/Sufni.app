using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Models;

public sealed record SessionTerrainInput(TerrainProfile Profile, TerrainBaseline Baseline, TelemetryTimeRange? AnalysisRange);

public enum SessionResponseState { NoTerrain, InsufficientBaseline, Reported, BaselineError }

/// <summary>
/// The bike response panel: the state of your usual in one sentence, the report once it is fitted, and the experimental
/// character hint, which needs no baseline.
/// </summary>
public sealed record SessionResponsePanel(SessionResponseState State, string Description, TerrainResponseReport? Report = null,
    string? Hint = null)
{
    public static SessionResponsePanel Hidden { get; } = new(SessionResponseState.NoTerrain, string.Empty);
    public bool IsVisible => State != SessionResponseState.NoTerrain;
}
