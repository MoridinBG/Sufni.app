using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Models;

public sealed record SessionTerrainInput(TerrainProfile Profile, TerrainBaseline Baseline, TelemetryTimeRange? AnalysisRange);

public enum SessionResponseState { NoTerrain, InsufficientBaseline, Reported, BaselineError }

/// <summary>The bike response panel: the state of your usual in one sentence, and the report once it is fitted.</summary>
public sealed record SessionResponsePanel(SessionResponseState State, string Description, TerrainResponseReport? Report = null)
{
    public static SessionResponsePanel Hidden { get; } = new(SessionResponseState.NoTerrain, string.Empty);
    public bool IsVisible => State != SessionResponseState.NoTerrain;
}
