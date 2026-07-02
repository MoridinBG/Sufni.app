using System;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;
using DampingSpeedCutoffSet = Sufni.App.ExtensionHost.Contracts.SessionDetails.DampingSpeedCutoffs;

namespace Sufni.App.Sessions.Analysis.Services;

public enum RecordedSessionAnalysisFamily
{
    DampingPercentages,
    SessionInsights,
    TravelDistribution,
    TravelFrequencyDistribution,
    VelocityDistribution,
    Balance,
    StrokeLengthDistribution,
    StrokeSpeedDistribution,
    DeepTravelDistribution,
    VibrationDistribution,
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

    public RecordedSessionAnalysisKey CreateKey(
        RecordedSessionAnalysisFamily family,
        SuspensionType? suspensionType = null,
        BalanceType? balanceType = null,
        ImuLocation? imuLocation = null) =>
        family switch
        {
            RecordedSessionAnalysisFamily.DampingPercentages => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: null,
                BalanceType: null,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: VelocityAverageMode,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffs,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.SessionInsights => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: null,
                BalanceType: null,
                ImuLocation: null,
                TravelDistributionMode: TravelDistributionMode,
                VelocityAverageMode: VelocityAverageMode,
                BalanceDisplacementMode: BalanceDisplacementMode,
                BalanceSpeedMode: BalanceSpeedMode,
                DampingSpeedCutoffs: DampingSpeedCutoffs,
                DampingPercentages: DampingPercentages,
                SessionInsightsTargetProfile: SessionInsightsTargetProfile),
            RecordedSessionAnalysisFamily.TravelDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: null,
                ImuLocation: null,
                TravelDistributionMode: TravelDistributionMode,
                VelocityAverageMode: null,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffSet.Default,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.TravelFrequencyDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: null,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: null,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffSet.Default,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.VelocityDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: null,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: VelocityAverageMode,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffs,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.Balance => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: null,
                BalanceType: balanceType,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: null,
                BalanceDisplacementMode: BalanceDisplacementMode,
                BalanceSpeedMode: BalanceSpeedMode,
                DampingSpeedCutoffs: DampingSpeedCutoffs,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.StrokeLengthDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: balanceType,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: null,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffSet.Default,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.StrokeSpeedDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: balanceType,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: null,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffSet.Default,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.DeepTravelDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: null,
                ImuLocation: null,
                TravelDistributionMode: null,
                VelocityAverageMode: null,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffSet.Default,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            RecordedSessionAnalysisFamily.VibrationDistribution => new RecordedSessionAnalysisKey(
                family,
                TelemetryGeneration,
                AnalysisRange,
                SuspensionType: suspensionType,
                BalanceType: null,
                ImuLocation: imuLocation,
                TravelDistributionMode: null,
                VelocityAverageMode: null,
                BalanceDisplacementMode: null,
                BalanceSpeedMode: null,
                DampingSpeedCutoffs: DampingSpeedCutoffSet.Default,
                DampingPercentages: null,
                SessionInsightsTargetProfile: null),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };
}

public sealed record RecordedSessionAnalysisKey(
    RecordedSessionAnalysisFamily Family,
    int TelemetryGeneration,
    TelemetryTimeRange? AnalysisRange,
    SuspensionType? SuspensionType,
    BalanceType? BalanceType,
    ImuLocation? ImuLocation,
    TravelDistributionMode? TravelDistributionMode,
    VelocityAverageMode? VelocityAverageMode,
    BalanceDisplacementMode? BalanceDisplacementMode,
    BalanceSpeedMode? BalanceSpeedMode,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    SessionDampingPercentages? DampingPercentages,
    SessionInsightsTargetProfile? SessionInsightsTargetProfile)
{
    public bool Matches(RecordedSessionAnalysisInputs inputs) =>
        this == inputs.CreateKey(Family, SuspensionType, BalanceType, ImuLocation);
}

public abstract record RecordedSessionAnalysisResult;

public sealed record DampingPercentagesAnalysisResult(
    SessionDampingPercentages Percentages) : RecordedSessionAnalysisResult;

public sealed record SessionInsightsAnalysisResult(
    SessionInsightsResult Insights) : RecordedSessionAnalysisResult;

public sealed record TravelDistributionAnalysisResult(
    HistogramData Histogram,
    TravelStatistics Statistics,
    double? MaxTravel,
    bool HasStrokeData) : RecordedSessionAnalysisResult;

public sealed record TravelFrequencyDistributionAnalysisResult(
    HistogramData Histogram,
    bool HasStrokeData) : RecordedSessionAnalysisResult;

public sealed record VelocityDistributionAnalysisResult(
    StackedHistogramData Histogram,
    VelocityStatistics Statistics,
    NormalDistributionData NormalDistribution,
    bool HasStrokeData) : RecordedSessionAnalysisResult;

public sealed record BalanceAnalysisResult(
    BalanceData Balance) : RecordedSessionAnalysisResult;

public sealed record StrokeLengthDistributionAnalysisResult(
    HistogramData Histogram,
    bool HasStrokeData) : RecordedSessionAnalysisResult;

public sealed record StrokeSpeedDistributionAnalysisResult(
    HistogramData Histogram,
    bool HasStrokeData) : RecordedSessionAnalysisResult;

public sealed record DeepTravelDistributionAnalysisResult(
    HistogramData Histogram,
    bool HasStrokeData) : RecordedSessionAnalysisResult;

public sealed record VibrationDistributionAnalysisResult(
    VibrationStats? Stats) : RecordedSessionAnalysisResult;

public sealed record RecordedSessionAnalysisResultChanged(
    RecordedSessionAnalysisKey Key,
    RecordedSessionAnalysisResult? Result,
    Exception? Error = null);
