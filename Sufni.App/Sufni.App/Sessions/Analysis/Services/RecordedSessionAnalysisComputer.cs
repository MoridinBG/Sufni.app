using Sufni.App.Terrain.Models;
using Sufni.App.Sessions.Plots;
using System.Collections.Generic;
using System.Linq;
using System;
using Sufni.App.Bikes.Models;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Services;

internal interface IRecordedSessionAnalysisComputer
{
    RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry, RecordedSessionAnalysisInputs? inputs = null);
}

internal sealed class RecordedSessionAnalysisComputer(
    ISessionInsightsService sessionInsightsService) : IRecordedSessionAnalysisComputer
{
    public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry, RecordedSessionAnalysisInputs? inputs = null)
    {
        var timeSelection = SelectionFor(key, inputs);
        return key.Family switch
        {
            RecordedSessionAnalysisFamily.DampingPercentages => new DampingPercentagesAnalysisResult(
                CalculateDampingPercentages(key, telemetry, timeSelection)),
            RecordedSessionAnalysisFamily.SessionInsights => CalculateSessionInsights(key, telemetry, timeSelection, inputs),
            RecordedSessionAnalysisFamily.Availability => CalculateAvailability(key, telemetry, timeSelection) with { TerrainSelection = inputs?.TerrainSelection?.Value },
            RecordedSessionAnalysisFamily.SelectionHighlights => CalculateHighlights(key, telemetry, timeSelection),
            _ => ComputePlot(key, telemetry, inputs),
        };

    }

    private static TelemetryTimeSelection? SelectionFor(RecordedSessionAnalysisKey key, RecordedSessionAnalysisInputs? inputs) =>
        key.Scope is AnalysisScope.ClassFilter ? inputs!.TerrainSelection!.Value.Selection : key.AnalysisRange;

    private static AnalysisAvailabilityResult CalculateAvailability(RecordedSessionAnalysisKey key, TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        var front = TelemetryStatistics.HasStrokeData(telemetry, SuspensionType.Front, timeSelection);
        var rear = TelemetryStatistics.HasStrokeData(telemetry, SuspensionType.Rear, timeSelection);
        var compression = TelemetryStatistics.HasBalanceData(telemetry, BalanceType.Compression, timeSelection);
        var rebound = TelemetryStatistics.HasBalanceData(telemetry, BalanceType.Rebound, timeSelection);
        var forkVibration = TelemetryStatistics.HasVibrationData(telemetry, ImuLocation.Fork);
        var frameVibration = TelemetryStatistics.HasVibrationData(telemetry, ImuLocation.Frame);
        var fullBalance = timeSelection is null ? compression || rebound
            : TelemetryStatistics.HasBalanceData(telemetry, BalanceType.Compression) || TelemetryStatistics.HasBalanceData(telemetry, BalanceType.Rebound);
        return new AnalysisAvailabilityResult(
            Availability(telemetry.Front.Present, front),
            Availability(telemetry.Rear.Present, rear),
            Availability(telemetry.Front.Present && telemetry.Rear.Present, compression),
            Availability(telemetry.Front.Present && telemetry.Rear.Present, rebound),
            Availability(telemetry.Front.Present && forkVibration, TelemetryStatistics.HasPairedVibrationSamples(telemetry, ImuLocation.Fork, SuspensionType.Front, timeSelection)),
            Availability(telemetry.Front.Present && frameVibration, TelemetryStatistics.HasPairedVibrationSamples(telemetry, ImuLocation.Frame, SuspensionType.Front, timeSelection)),
            Availability(telemetry.Rear.Present && forkVibration, TelemetryStatistics.HasPairedVibrationSamples(telemetry, ImuLocation.Fork, SuspensionType.Rear, timeSelection)),
            Availability(telemetry.Rear.Present && frameVibration, TelemetryStatistics.HasPairedVibrationSamples(telemetry, ImuLocation.Frame, SuspensionType.Rear, timeSelection)), fullBalance);

        static AnalysisDataAvailability Availability(bool present, bool hasData) => !present
            ? AnalysisDataAvailability.Absent : hasData ? AnalysisDataAvailability.Available : AnalysisDataAvailability.Empty;
    }

    private static SelectionHighlightsAnalysisResult CalculateHighlights(RecordedSessionAnalysisKey key, TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        var ranges = new List<TelemetryHighlightRange>();
        Add(key.FrontSelection);
        Add(key.RearSelection);
        return new SelectionHighlightsAnalysisResult([.. TelemetryStatistics.MergeHighlightRanges(ranges)]);

        void Add(TelemetryRangeSelection? selection)
        {
            if (selection is null) return;
            ranges.AddRange(TelemetryStatistics.CalculateHighlightRanges(telemetry, selection, timeSelection)
                .Select(range => range with { SuspensionType = selection.SuspensionType }));
        }
    }

    internal static RecordedSessionAnalysisResult ComputePlot(RecordedSessionAnalysisKey key, TelemetryData telemetry, RecordedSessionAnalysisInputs? inputs = null)
    {
        var timeSelection = SelectionFor(key, inputs);
        return key.Family switch
        {
            RecordedSessionAnalysisFamily.TravelDistribution => CalculateTravelDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.TravelFrequencyDistribution => CalculateTravelFrequencyDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.VelocityDistribution => CalculateVelocityDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.Balance => CalculateBalance(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.StrokeLengthDistribution => CalculateStrokeLengthDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.StrokeSpeedDistribution => CalculateStrokeSpeedDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.DeepTravelDistribution => CalculateDeepTravelDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.VibrationDistribution => CalculateVibrationDistribution(key, telemetry, timeSelection),
            RecordedSessionAnalysisFamily.ImuDisplayProjection => new ImuDisplayProjectionAnalysisResult(
                ImuDisplaySignalProcessor.ProcessRecorded(telemetry, inputs?.TrackSamples?.Value)),
            RecordedSessionAnalysisFamily.PositionVelocity => new PositionVelocityAnalysisResult(
                PositionVelocityTraceBuilder.Build(telemetry, timeSelection)),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Family, null),
        };

    }

    private SessionDampingPercentages CalculateDampingPercentages(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        return CalculateDampingPercentages(
            telemetry,
            timeSelection,
            key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged,
            key.DampingSpeedCutoffs);
    }

    internal static SessionDampingPercentages CalculateDampingPercentages(
        TelemetryData telemetryData,
        TelemetryTimeSelection? range = null,
        VelocityAverageMode velocityAverageMode = VelocityAverageMode.SampleAveraged,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        var cutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        return SessionDampingPercentages.FromSides(
            CalculateDampingSidePercentages(telemetryData, SuspensionType.Front, range, velocityAverageMode, cutoffs.Front),
            CalculateDampingSidePercentages(telemetryData, SuspensionType.Rear, range, velocityAverageMode, cutoffs.Rear));
    }

    private SessionInsightsAnalysisResult CalculateSessionInsights(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection, RecordedSessionAnalysisInputs? inputs)
    {
        var request = new SessionInsightsRequest(
            telemetry,
            timeSelection,
            key.TravelDistributionMode ?? TravelDistributionMode.ActiveSuspension,
            key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged,
            key.BalanceDisplacementMode ?? BalanceDisplacementMode.Zenith,
            key.BalanceSpeedMode ?? BalanceSpeedMode.Both,
            key.SessionInsightsTargetProfile ?? SessionInsightsTargetProfile.Trail)
        {
            DampingSpeedCutoffs = key.DampingSpeedCutoffs,
            Terrain = inputs?.TerrainProfile is { } profile && key.Baseline is { } baseline
                ? new(profile, baseline, key.AnalysisRange) : null,
        };

        return new SessionInsightsAnalysisResult(sessionInsightsService.Analyze(request));
    }

    internal static TravelDistributionAnalysisResult CalculateTravelDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection = null)
    {
        timeSelection ??= key.AnalysisRange;
        var side = RequireSuspensionType(key);
        var options = new TravelStatisticsOptions(
            timeSelection,
            key.TravelDistributionMode ?? TravelDistributionMode.ActiveSuspension);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, timeSelection);
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
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        var side = RequireSuspensionType(key);
        return new(TelemetryStatistics.CalculateTravelFrequencyHistogram(telemetry, side, timeSelection));
    }

    internal static VelocityDistributionAnalysisResult CalculateVelocityDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection = null)
    {
        timeSelection ??= key.AnalysisRange;
        var side = RequireSuspensionType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, timeSelection);
        if (!hasStrokeData)
        {
            return new VelocityDistributionAnalysisResult(
                new StackedHistogramData([], []),
                new VelocityStatistics(0, 0, 0, 0),
                new NormalDistributionData([], []),
                HasStrokeData: false);
        }

        var options = CreateVelocityOptions(key, side, timeSelection);
        var averageMode = key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged;
        return new VelocityDistributionAnalysisResult(
            TelemetryStatistics.CalculateVelocityHistogram(telemetry, side, options),
            TelemetryStatistics.CalculateVelocityStatistics(telemetry, side, options),
            averageMode == VelocityAverageMode.SampleAveraged
                ? TelemetryStatistics.CalculateNormalDistribution(telemetry, side, timeSelection)
                : new NormalDistributionData([], []),
            HasStrokeData: true);
    }

    internal static BalanceAnalysisResult CalculateBalance(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection = null)
    {
        timeSelection ??= key.AnalysisRange;
        return new BalanceAnalysisResult(TelemetryStatistics.CalculateBalance(
            telemetry,
            RequireBalanceType(key),
            new BalanceStatisticsOptions(
                timeSelection,
                key.BalanceDisplacementMode ?? BalanceDisplacementMode.Zenith,
                key.BalanceSpeedMode ?? BalanceSpeedMode.Both,
                key.DampingSpeedCutoffs.Front.CompressionMmPerSecond,
                key.DampingSpeedCutoffs.Front.ReboundMmPerSecond,
                key.DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
                key.DampingSpeedCutoffs.Rear.ReboundMmPerSecond)));
    }

    private static StrokeLengthDistributionAnalysisResult CalculateStrokeLengthDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection = null)
    {
        timeSelection ??= key.AnalysisRange;
        var side = RequireSuspensionType(key);
        var strokeKind = RequireBalanceType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, timeSelection);
        return new StrokeLengthDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateStrokeLengthHistogram(telemetry, side, strokeKind, timeSelection)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static StrokeSpeedDistributionAnalysisResult CalculateStrokeSpeedDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        var side = RequireSuspensionType(key);
        var strokeKind = RequireBalanceType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, timeSelection);
        return new StrokeSpeedDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateStrokeSpeedHistogram(telemetry, side, strokeKind, timeSelection)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static DeepTravelDistributionAnalysisResult CalculateDeepTravelDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        var side = RequireSuspensionType(key);
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetry, side, timeSelection);
        return new DeepTravelDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, side, timeSelection)
                : new HistogramData([], []),
            hasStrokeData);
    }

    private static VibrationDistributionAnalysisResult CalculateVibrationDistribution(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry, TelemetryTimeSelection? timeSelection)
    {
        return new VibrationDistributionAnalysisResult(TelemetryStatistics.CalculateVibration(
            telemetry,
            key.ImuLocation ?? throw new ArgumentException("Analysis key requires an IMU location.", nameof(key)),
            RequireSuspensionType(key),
            timeSelection));
    }

    private static VelocityStatisticsOptions CreateVelocityOptions(
        RecordedSessionAnalysisKey key,
        SuspensionType side, TelemetryTimeSelection? timeSelection)
    {
        var cutoffs = key.DampingSpeedCutoffs.ForSide(side);
        return new VelocityStatisticsOptions(
            timeSelection,
            key.VelocityAverageMode ?? VelocityAverageMode.SampleAveraged,
            cutoffs.CompressionMmPerSecond,
            cutoffs.ReboundMmPerSecond);
    }

    private static SuspensionType RequireSuspensionType(RecordedSessionAnalysisKey key) =>
        key.SuspensionType ?? throw new ArgumentException("Analysis key requires a suspension type.", nameof(key));

    private static BalanceType RequireBalanceType(RecordedSessionAnalysisKey key) =>
        key.BalanceType ?? throw new ArgumentException("Analysis key requires a balance type.", nameof(key));

    private static SessionDampingSidePercentages CalculateDampingSidePercentages(
        TelemetryData telemetryData,
        SuspensionType suspensionType,
        TelemetryTimeSelection? range,
        VelocityAverageMode velocityAverageMode,
        DampingSpeedCutoffSide cutoffs)
    {
        if (!TelemetryStatistics.HasStrokeData(telemetryData, suspensionType, range))
        {
            return SessionDampingSidePercentages.Empty;
        }

        var options = new VelocityStatisticsOptions(
            range,
            velocityAverageMode,
            cutoffs.CompressionMmPerSecond,
            cutoffs.ReboundMmPerSecond);
        var bands = TelemetryStatistics.CalculateVelocityBands(
            telemetryData,
            suspensionType,
            options);
        return new SessionDampingSidePercentages(
            bands.HighSpeedCompression,
            bands.LowSpeedCompression,
            bands.LowSpeedRebound,
            bands.HighSpeedRebound);
    }
}
