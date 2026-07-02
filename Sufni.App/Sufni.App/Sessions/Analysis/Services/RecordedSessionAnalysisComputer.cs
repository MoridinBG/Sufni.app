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
            RecordedSessionAnalysisFamily.TravelDistribution => CalculateTravelDistribution(key, telemetry),
            RecordedSessionAnalysisFamily.TravelFrequencyDistribution => CalculateTravelFrequencyDistribution(key, telemetry),
            RecordedSessionAnalysisFamily.VelocityDistribution => CalculateVelocityDistribution(key, telemetry),
            RecordedSessionAnalysisFamily.Balance => CalculateBalance(key, telemetry),
            RecordedSessionAnalysisFamily.StrokeLengthDistribution => CalculateStrokeLengthDistribution(key, telemetry),
            RecordedSessionAnalysisFamily.StrokeSpeedDistribution => CalculateStrokeSpeedDistribution(key, telemetry),
            RecordedSessionAnalysisFamily.DeepTravelDistribution => CalculateDeepTravelDistribution(key, telemetry),
            RecordedSessionAnalysisFamily.VibrationDistribution => CalculateVibrationDistribution(key, telemetry),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Family, null),
        };

    private SessionDampingPercentages CalculateDampingPercentages(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        return sessionPresentationService.CalculateDampingPercentages(
            telemetry,
            key.AnalysisRange,
            key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged,
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
            key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged,
            key.BalanceDisplacementMode ?? BalanceDisplacementMode.Zenith,
            key.BalanceSpeedMode ?? BalanceSpeedMode.Both,
            key.DampingPercentages ?? SessionDampingPercentages.Empty,
            key.SessionInsightsTargetProfile ?? SessionInsightsTargetProfile.Trail)
        {
            DampingSpeedCutoffs = key.DampingSpeedCutoffs,
        };

        return new SessionInsightsAnalysisResult(sessionInsightsService.Analyze(request));
    }

    private static TravelDistributionAnalysisResult CalculateTravelDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var side = RequireSuspensionType(key);
        var options = new TravelStatisticsOptions(
            key.AnalysisRange,
            key.TravelDistributionMode ?? TravelDistributionMode.ActiveSuspension);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, key.AnalysisRange);
        var suspension = side == SuspensionType.Front ? telemetry.Front : telemetry.Rear;
        if (options.HistogramMode == TravelDistributionMode.ActiveSuspension && !hasStrokeData)
        {
            return new TravelDistributionAnalysisResult(
                new HistogramData([], []),
                new TravelStatistics(0, 0, 0),
                suspension.MaxTravel,
                HasStrokeData: false);
        }

        return new TravelDistributionAnalysisResult(
            TelemetryStatistics.CalculateTravelHistogram(telemetry, side, options),
            TelemetryStatistics.CalculateTravelStatistics(telemetry, side, options),
            suspension.MaxTravel,
            hasStrokeData);
    }

    private static TravelFrequencyDistributionAnalysisResult CalculateTravelFrequencyDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var side = RequireSuspensionType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, key.AnalysisRange);
        return new TravelFrequencyDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateTravelFrequencyHistogram(telemetry, side, key.AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static VelocityDistributionAnalysisResult CalculateVelocityDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var side = RequireSuspensionType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, key.AnalysisRange);
        if (!hasStrokeData)
        {
            return new VelocityDistributionAnalysisResult(
                new StackedHistogramData([], []),
                new VelocityStatistics(0, 0, 0, 0),
                new NormalDistributionData([], []),
                HasStrokeData: false);
        }

        var options = CreateVelocityOptions(key, side);
        var averageMode = key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged;
        return new VelocityDistributionAnalysisResult(
            TelemetryStatistics.CalculateVelocityHistogram(telemetry, side, options),
            TelemetryStatistics.CalculateVelocityStatistics(telemetry, side, options),
            averageMode == VelocityAverageMode.SampleAveraged
                ? TelemetryStatistics.CalculateNormalDistribution(telemetry, side, key.AnalysisRange)
                : new NormalDistributionData([], []),
            HasStrokeData: true);
    }

    private static BalanceAnalysisResult CalculateBalance(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        return new BalanceAnalysisResult(TelemetryStatistics.CalculateBalance(
            telemetry,
            RequireBalanceType(key),
            new BalanceStatisticsOptions(
                key.AnalysisRange,
                key.BalanceDisplacementMode ?? BalanceDisplacementMode.Zenith,
                key.BalanceSpeedMode ?? BalanceSpeedMode.Both,
                key.DampingSpeedCutoffs.Front.CompressionMmPerSecond,
                key.DampingSpeedCutoffs.Front.ReboundMmPerSecond,
                key.DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
                key.DampingSpeedCutoffs.Rear.ReboundMmPerSecond)));
    }

    private static StrokeLengthDistributionAnalysisResult CalculateStrokeLengthDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var side = RequireSuspensionType(key);
        var strokeKind = RequireBalanceType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, key.AnalysisRange);
        return new StrokeLengthDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateStrokeLengthHistogram(telemetry, side, strokeKind, key.AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static StrokeSpeedDistributionAnalysisResult CalculateStrokeSpeedDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var side = RequireSuspensionType(key);
        var strokeKind = RequireBalanceType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, key.AnalysisRange);
        return new StrokeSpeedDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateStrokeSpeedHistogram(telemetry, side, strokeKind, key.AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static DeepTravelDistributionAnalysisResult CalculateDeepTravelDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        var side = RequireSuspensionType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, key.AnalysisRange);
        return new DeepTravelDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, side, key.AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static VibrationDistributionAnalysisResult CalculateVibrationDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry)
    {
        return new VibrationDistributionAnalysisResult(TelemetryStatistics.CalculateVibration(
            telemetry,
            key.ImuLocation ?? throw new ArgumentException("Analysis key requires an IMU location.", nameof(key)),
            RequireSuspensionType(key),
            key.AnalysisRange));
    }

    private static VelocityStatisticsOptions CreateVelocityOptions(
        RecordedSessionAnalysisKey key,
        SuspensionType side)
    {
        var cutoffs = key.DampingSpeedCutoffs.ForSide(side);
        return new VelocityStatisticsOptions(
            key.AnalysisRange,
            key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged,
            cutoffs.CompressionMmPerSecond,
            cutoffs.ReboundMmPerSecond);
    }

    private static SuspensionType RequireSuspensionType(RecordedSessionAnalysisKey key) =>
        key.SuspensionType ?? throw new ArgumentException("Analysis key requires a suspension type.", nameof(key));

    private static BalanceType RequireBalanceType(RecordedSessionAnalysisKey key) =>
        key.BalanceType ?? throw new ArgumentException("Analysis key requires a balance type.", nameof(key));
}
