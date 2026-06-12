using System.Linq;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services.SessionAnalysis;

/// <summary>
/// Orchestrates the session analysis: builds the analysis context, runs the
/// typed heuristics (<see cref="SessionDiagnostics"/>), orders the findings,
/// and materializes the presentation contract through
/// <see cref="SessionAnalysisPresenter"/>.
/// </summary>
public sealed class SessionAnalysisService : ISessionAnalysisService
{
    public SessionAnalysisResult Analyze(SessionAnalysisRequest request)
    {
        if (request.TelemetryData is null)
        {
            return SessionAnalysisResult.Hidden;
        }

        var travelOptions = new TravelStatisticsOptions(request.AnalysisRange, request.TravelHistogramMode);
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

        return SessionAnalysisPresenter.Present(report with { Findings = orderedFindings }, context);
    }
}
