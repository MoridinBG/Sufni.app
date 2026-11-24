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
            $"Your usual needs {SuspensionResponseModel.MinimumWindows:N0} usable windows: {report.Windows:N0} so far.",
        SessionResponseState.Reported =>
            $"Your usual comes from {report.Sessions} other rides on this bike ({report.Windows:N0} windows).",
        _ => string.Empty
    };
}
