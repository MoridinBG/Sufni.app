using System.Collections.Generic;
using System.Linq;
using Sufni.App.Shared.Formatting;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

internal static partial class SessionInsightsTextCatalog
{
    public static string ResponseDescription(TerrainResponseDiagnostics report) => report.State switch
    {
        SessionResponseState.BaselineError => "Your usual could not be loaded.",
        SessionResponseState.InsufficientBaseline when report.LacksVariation =>
            "Your usual needs rides where speed and roughness vary independently.",
        SessionResponseState.InsufficientBaseline when report.Sessions < SuspensionResponseModel.MinimumSessions =>
            $"Your usual needs {SuspensionResponseModel.MinimumSessions} rides on this bike: {report.Sessions} of {SuspensionResponseModel.MinimumSessions} so far.",
        SessionResponseState.InsufficientBaseline =>
            $"Your usual needs {Riding(SuspensionResponseModel.MinimumWindows)} of descent riding: {Riding(report.Windows)} so far.",
        SessionResponseState.Reported =>
            $"Your usual comes from {report.Sessions} other rides on this bike, {Riding(report.Windows)} of descent riding.{Sensors(report.Rows)}",
        _ => string.Empty
    };

    public const string ResponseWithoutTerrain = "Bike response needs a terrain profile for this ride.";
    public const string ResponseBaselineLoading = "Your usual is still loading.";

    private static string Riding(int windows) => UnitsFormatter.FormatDistance(windows * TerrainProfileCalculator.WindowMetres);

    private static string Sensors(IEnumerable<TerrainResponseRow> rows)
    {
        var cells = rows.SelectMany(row => row.Cells).Where(cell => cell.Target != ResponseTarget.PassedToFrame).ToArray();
        var parts = new List<string>();
        Add(cells.Where(cell => TerrainResponseText.IsFork(cell.Target)), "the fork");
        Add(cells.Where(cell => !TerrainResponseText.IsFork(cell.Target)), "the shock");
        return parts.Count == 0 ? string.Empty : $" It reads the ground from {string.Join(" and ", parts)}.";

        void Add(IEnumerable<ResponseCell> end, string name)
        {
            var sensors = end.Select(cell => cell.Predictors).Distinct().ToArray();
            if (sensors.Length == 1) parts.Add($"{SensorName(sensors[0])} for {name}");
        }
    }

    private static string SensorName(GroundSensors sensors) => sensors switch
    {
        GroundSensors.ForkAndRearAxle => "the fork and rear axle IMUs",
        GroundSensors.Fork => "the fork IMU",
        GroundSensors.RearAxle => "the rear axle IMU",
        _ => "the frame IMU"
    };

    public static string? GroundComfortNote(SessionTerrainInput? terrain) => terrain switch
    {
        null => "Ground & comfort needs a terrain profile for this ride.",
        { Scope: null } => "Ground & comfort is not available in a segment comparison.",
        _ => null
    };

    public static string? CharacterHint(TerrainCharacter character)
    {
        var parts = new List<string>();
        Add(character.Rear, "rear");
        Add(character.Front, "fork");
        return parts.Count == 0 ? null : $"Experimental: most rough ground here was {string.Join(" and ", parts)}.";

        void Add(CharacterShares? shares, string end)
        {
            if (shares?.Dominant is not { } dominant) return;
            var (name, share) = dominant == StrokeCharacter.BigHits ? ("big hits", shares.BigHitShare) : ("chatter", shares.ChatterShare);
            parts.Add($"{name} at the {end} ({share:P0})");
        }
    }
}
