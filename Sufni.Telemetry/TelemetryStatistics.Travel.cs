using System.Diagnostics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static HistogramData CalculateTravelHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        return CalculateTravelHistogram(telemetryData, type, new TravelStatisticsOptions(range));
    }

    public static HistogramData CalculateTravelHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TravelStatisticsOptions options)
    {
        var suspension = GetSuspension(telemetryData, type);

        return options.HistogramMode switch
        {
            TravelHistogramMode.DynamicSag => CalculateDynamicSagTravelHistogram(telemetryData, suspension, options.Range),
            TravelHistogramMode.ActiveSuspension => CalculateActiveSuspensionTravelHistogram(telemetryData, suspension, options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.HistogramMode, null),
        };
    }

    public static HistogramData CalculateStrokeLengthHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        BalanceType strokeKind,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var strokes = GetIncludedStrokes(telemetryData, suspension, strokeKind, range);

        var hist = new double[suspension.TravelBins.Length - 1];
        foreach (var stroke in strokes)
        {
            var length = Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]);
            hist[HistogramBuilder.DigitizeValue(length, suspension.TravelBins)] += 1;
        }

        if (strokes.Length > 0)
        {
            hist = hist.Select(value => value / strokes.Length * 100.0).ToArray();
        }

        return new HistogramData(suspension.TravelBins.ToList(), [.. hist]);
    }

    public static HistogramData CalculateStrokeSpeedHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        BalanceType strokeKind,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var strokes = GetIncludedStrokes(telemetryData, suspension, strokeKind, range);

        var maxSpeed = strokes.Length == 0
            ? Parameters.VelocityHistStep
            : strokes.Select(stroke => Math.Abs(stroke.Stat.MaxVelocity)).Max();
        var maxBin = Math.Max(
            Parameters.VelocityHistStep,
            Math.Ceiling(maxSpeed / Parameters.VelocityHistStep) * Parameters.VelocityHistStep);
        var bins = HistogramBuilder.Linspace(0, maxBin, (int)(maxBin / Parameters.VelocityHistStep) + 1);
        var hist = new double[bins.Length - 1];

        foreach (var stroke in strokes)
        {
            var speed = Math.Abs(stroke.Stat.MaxVelocity);
            hist[HistogramBuilder.DigitizeValue(speed, bins)] += 1;
        }

        if (strokes.Length > 0)
        {
            hist = hist.Select(value => value / strokes.Length * 100.0).ToArray();
        }

        return new HistogramData(bins.ToList(), [.. hist]);
    }

    public static HistogramData CalculateDeepTravelHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        Debug.Assert(suspension.MaxTravel is not null);

        var bins = suspension.TravelBins[^6..];
        var hist = new double[bins.Length - 1];
        var threshold = Parameters.DeepTravelThresholdRatio * suspension.MaxTravel.Value;

        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range))
        {
            if (stroke.Stat.MaxTravel < threshold)
            {
                continue;
            }

            hist[HistogramBuilder.DigitizeValue(stroke.Stat.MaxTravel, bins)] += 1;
        }

        return new HistogramData(bins.ToList(), [.. hist]);
    }

    public static TravelStatistics CalculateTravelStatistics(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        return CalculateTravelStatistics(telemetryData, type, new TravelStatisticsOptions(range));
    }

    public static TravelStatistics CalculateTravelStatistics(
        TelemetryData telemetryData,
        SuspensionType type,
        TravelStatisticsOptions options)
    {
        var suspension = GetSuspension(telemetryData, type);

        return options.HistogramMode switch
        {
            TravelHistogramMode.DynamicSag => CalculateDynamicSagTravelStatistics(telemetryData, suspension, options.Range),
            TravelHistogramMode.ActiveSuspension => CalculateActiveSuspensionTravelStatistics(telemetryData, suspension, options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.HistogramMode, null),
        };
    }

    private static HistogramData CalculateActiveSuspensionTravelHistogram(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var histogram = new double[suspension.TravelBins.Length - 1];
        var totalCount = 0;

        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range).Concat(GetIncludedRebounds(telemetryData, suspension, range)))
        {
            totalCount += stroke.Stat.Count;
            foreach (var digitizedTravel in stroke.DigitizedTravel)
            {
                histogram[digitizedTravel] += 1;
            }
        }

        if (totalCount <= 0)
        {
            return new HistogramData(suspension.TravelBins.ToList(), [.. histogram]);
        }

        histogram = histogram.Select(value => value / totalCount * 100.0).ToArray();
        return new HistogramData(suspension.TravelBins.ToList(), [.. histogram]);
    }

    private static HistogramData CalculateDynamicSagTravelHistogram(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var histogram = new double[suspension.TravelBins.Length - 1];
        var travelSamples = GetTravelSamples(telemetryData, suspension, range);

        foreach (var travel in travelSamples)
        {
            histogram[HistogramBuilder.DigitizeValue(travel, suspension.TravelBins)] += 1;
        }

        if (travelSamples.Length > 0)
        {
            histogram = histogram.Select(value => value / travelSamples.Length * 100.0).ToArray();
        }

        return new HistogramData(suspension.TravelBins.ToList(), [.. histogram]);
    }

    private static TravelStatistics CalculateActiveSuspensionTravelStatistics(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var sum = 0.0;
        var count = 0.0;
        var max = 0.0;
        var bottomouts = 0;

        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range).Concat(GetIncludedRebounds(telemetryData, suspension, range)))
        {
            sum += stroke.Stat.SumTravel;
            count += stroke.Stat.Count;
            bottomouts += stroke.Stat.Bottomouts;
            if (stroke.Stat.MaxTravel > max)
            {
                max = stroke.Stat.MaxTravel;
            }
        }

        if (count <= 0)
        {
            return new TravelStatistics(0, 0, 0);
        }

        return new TravelStatistics(max, sum / count, bottomouts);
    }

    private static TravelStatistics CalculateDynamicSagTravelStatistics(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var travelSamples = GetTravelSamples(telemetryData, suspension, range);
        if (travelSamples.Length == 0)
        {
            return new TravelStatistics(0, 0, 0);
        }

        return new TravelStatistics(
            travelSamples.Max(),
            travelSamples.Average(),
            CountBottomouts(travelSamples, suspension.MaxTravel));
    }

    private static int CountBottomouts(double[] travelSamples, double? maxTravel)
    {
        if (maxTravel is null)
        {
            return 0;
        }

        var bottomouts = 0;
        var threshold = maxTravel.Value - Parameters.BottomoutThreshold;
        for (var index = 0; index < travelSamples.Length; index++)
        {
            if (!(travelSamples[index] > threshold))
            {
                continue;
            }

            bottomouts += 1;
            for (; index < travelSamples.Length && travelSamples[index] > threshold; index++) { }
        }

        return bottomouts;
    }
}
