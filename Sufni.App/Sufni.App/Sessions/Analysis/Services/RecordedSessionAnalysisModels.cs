using System;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Services;

public enum RecordedSessionAnalysisFamily
{
    DampingPercentages,
    SessionInsights,
}

public sealed record RecordedSessionAnalysisInputs(
    int TelemetryGeneration,
    TelemetryTimeRange? AnalysisRange,
    TravelDistributionMode TravelDistributionMode,
    VelocityAverageMode VelocityAverageMode,
    BalanceDisplacementMode BalanceDisplacementMode,
    BalanceSpeedMode BalanceSpeedMode,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    SessionDampingPercentages DampingPercentages,
    SessionInsightsTargetProfile SessionInsightsTargetProfile)
{
    public RecordedSessionAnalysisKey DampingPercentagesKey => CreateKey(RecordedSessionAnalysisFamily.DampingPercentages);

    public RecordedSessionAnalysisKey SessionInsightsKey => CreateKey(RecordedSessionAnalysisFamily.SessionInsights);

    public RecordedSessionAnalysisKey CreateKey(RecordedSessionAnalysisFamily family) =>
        family switch
        {
            RecordedSessionAnalysisFamily.DampingPercentages => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                TravelDistributionMode: null,
                VelocityAverageMode,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.SessionInsights => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                TravelDistributionMode,
                VelocityAverageMode,
                BalanceDisplacementMode,
                BalanceSpeedMode,
                DampingSpeedCutoffs,
                DampingPercentages,
                SessionInsightsTargetProfile),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };
}

public sealed record RecordedSessionAnalysisKey(
    RecordedSessionAnalysisFamily Family,
    int TelemetryGeneration,
    TelemetryTimeRange? AnalysisRange,
    TravelDistributionMode? TravelDistributionMode,
    VelocityAverageMode VelocityAverageMode,
    BalanceDisplacementMode? BalanceDisplacementMode,
    BalanceSpeedMode? BalanceSpeedMode,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    SessionDampingPercentages? DampingPercentages,
    SessionInsightsTargetProfile? SessionInsightsTargetProfile)
{
    public bool Matches(RecordedSessionAnalysisInputs inputs) => this == inputs.CreateKey(Family);
}

public abstract record RecordedSessionAnalysisResult;

public sealed record DampingPercentagesAnalysisResult(
    SessionDampingPercentages Percentages) : RecordedSessionAnalysisResult;

public sealed record SessionInsightsAnalysisResult(
    SessionInsightsResult Insights) : RecordedSessionAnalysisResult;

public sealed record RecordedSessionAnalysisResultChanged(
    RecordedSessionAnalysisKey Key,
    RecordedSessionAnalysisResult? Result,
    Exception? Error = null);
