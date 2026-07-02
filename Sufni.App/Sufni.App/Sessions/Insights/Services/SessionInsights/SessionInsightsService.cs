using System.Linq;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

/// <summary>
/// Orchestrates the session analysis: builds the analysis context, runs the
/// typed heuristics (<see cref="SessionDiagnostics"/>), orders the findings,
/// and materializes the presentation contract through
/// <see cref="SessionInsightsPresenter"/>.
/// </summary>
public sealed class SessionInsightsService : ISessionInsightsService
{
    public SessionInsightsResult Analyze(SessionInsightsRequest request)
    {
        if (request.TelemetryData is null)
        {
            return SessionInsightsResult.Hidden;
        }

        var travelOptions = new TravelStatisticsOptions(request.AnalysisRange, request.TravelDistributionMode);
        var velocityOptions = new VelocityStatisticsOptions(request.AnalysisRange, request.VelocityAverageMode);
        var balanceOptions = new BalanceStatisticsOptions(
            request.AnalysisRange,
            request.BalanceDisplacementMode,
            request.BalanceSpeedMode,
            request.DampingSpeedCutoffs.Front.CompressionMmPerSecond,
            request.DampingSpeedCutoffs.Front.ReboundMmPerSecond,
            request.DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
            request.DampingSpeedCutoffs.Rear.ReboundMmPerSecond);
        var context = new AnalysisContext(
            request,
            travelOptions,
            velocityOptions,
            balanceOptions,
            SessionDiagnostics.GetProfileReferences(request.TargetProfile));

        var report = SessionDiagnostics.Run(request.TelemetryData, context);
        var orderedFindings = report.Findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Category)
            .ToArray();

        return SessionInsightsPresenter.Present(report with { Findings = orderedFindings }, context);
    }
}
