using System;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Services;

internal interface IRecordedSessionAnalysisComputer
{
    RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry);
}

internal sealed class RecordedSessionAnalysisComputer(
    ISessionPresentationService sessionPresentationService,
    ISessionInsightsService sessionInsightsService) : IRecordedSessionAnalysisComputer
{
    public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry) =>
        key.Family switch
        {
            RecordedSessionAnalysisFamily.DampingPercentages => new DampingPercentagesAnalysisResult(
                CalculateDampingPercentages(key, telemetry)),
            RecordedSessionAnalysisFamily.SessionInsights => CalculateSessionInsights(key, telemetry),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Family, null),
        };

    private SessionDampingPercentages CalculateDampingPercentages(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        return sessionPresentationService.CalculateDampingPercentages(
            telemetry,
            key.AnalysisRange,
            key.VelocityAverageMode,
            key.DampingSpeedCutoffs);
    }

    private SessionInsightsAnalysisResult CalculateSessionInsights(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var request = new SessionInsightsRequest(
            telemetry,
            key.AnalysisRange,
            key.TravelDistributionMode ?? TravelDistributionMode.ActiveSuspension,
            key.VelocityAverageMode,
            key.BalanceDisplacementMode ?? BalanceDisplacementMode.Zenith,
            key.BalanceSpeedMode ?? BalanceSpeedMode.Both,
            key.DampingPercentages ?? SessionDampingPercentages.Empty,
            key.SessionInsightsTargetProfile ?? SessionInsightsTargetProfile.Trail)
        {
            DampingSpeedCutoffs = key.DampingSpeedCutoffs,
        };

        return new SessionInsightsAnalysisResult(sessionInsightsService.Analyze(request));
    }
}
