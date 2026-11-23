using Sufni.App.Terrain.ViewModels;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

internal static partial class SessionInsightsTextCatalog
{
    public static string ResponseDescription(TerrainResponseDiagnostics report) => report.State switch
    {
        SessionResponseState.BaselineError => "The bike baseline could not be loaded.",
        SessionResponseState.InsufficientBaseline when report.LacksVariation =>
            $"The {report.Sessions} baseline rides cover {report.Windows:N0} usable windows. Speed and ground vibration need to vary independently to estimate the response.",
        SessionResponseState.InsufficientBaseline =>
            $"Baseline coverage is {report.Sessions} rides and {report.Windows:N0} usable windows. Each model needs {SuspensionResponseModel.MinimumSessions} rides and {SuspensionResponseModel.MinimumWindows:N0} compatible windows.",
        SessionResponseState.Reported =>
            $"Ratios compare this ride with {report.Sessions} other rides on this bike ({report.Windows:N0} windows). A ratio of 1.00 matches the predicted response. The same-position comparison is available in segment analysis.",
        _ => string.Empty
    };

    public static string ResponseCoverage(TerrainResponseRow row) => row.MeanSpeed is { } speed
        ? $"{row.DistanceMetres:0.#} m · {row.Share:P0} · {speed * 3.6:0.#} km/h" : "0 m";

    public static SessionResponseCell ResponseCell(ResponseCell cell)
    {
        var predictor = cell.Predictor == ImuLocation.Fork ? "lowers" : "frame";
        var value = TerrainResponseText.Value(cell.State, cell.Ratio);
        var detail = cell.State == ResponseCellState.WithinRepeatRange ? "Within repeat range. " : string.Empty;
        return new($"{TerrainResponseText.TargetLabel(cell.Target)} · {predictor}", value,
            $"{detail}{cell.BaselineSessions} baseline rides, {cell.BaselineWindows:N0} usable windows.", cell);
    }
}
