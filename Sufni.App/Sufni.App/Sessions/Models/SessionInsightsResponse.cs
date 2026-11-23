using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Models;

public sealed record SessionTerrainInput(TerrainProfile Profile, TerrainBaseline Baseline, TelemetryTimeRange? AnalysisRange);

public enum SessionResponseState { NoTerrain, InsufficientBaseline, Reported, BaselineError }

public sealed record SessionResponsePanel(SessionResponseState State, string Description, int BaselineSessions, int BaselineWindows,
    ImmutableArray<SessionResponseRow> Rows)
{
    public static SessionResponsePanel Hidden { get; } = new(SessionResponseState.NoTerrain, string.Empty, 0, 0, []);
    public bool IsVisible => State != SessionResponseState.NoTerrain;
}

public sealed record SessionResponseRow(string Label, string Coverage, TerrainResponseRow Measurement,
    ImmutableArray<SessionResponseCell> Cells);

public sealed record SessionResponseCell(string Label, string Value, string Detail, ResponseCell Measurement)
{
    public bool IsFinding => Measurement.State == ResponseCellState.Finding;
}
