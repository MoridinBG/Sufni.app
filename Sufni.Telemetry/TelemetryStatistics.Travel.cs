using System.Collections.Immutable;
using System.Diagnostics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static HistogramData CalculateTravelHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeSelection? range = null)
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
            TravelDistributionMode.DynamicSag => CalculateDynamicSagTravelHistogram(telemetryData, suspension, options.Selection),
            TravelDistributionMode.ActiveSuspension => CalculateActiveSuspensionTravelHistogram(
                telemetryData,
                suspension,
                options.Selection,
                telemetryData.CoarseStrokeIndexes),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.HistogramMode, null),
        };
    }

    public static HistogramData CalculateStrokeLengthHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        BalanceType strokeKind,
        TelemetryTimeSelection? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var strokes = GetIncludedStrokes(telemetryData, suspension, strokeKind, range);

        var hist = new double[suspension.TravelBins.Length - 1];
        foreach (var stroke in strokes)
        {
            var length = Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]);
            hist[HistogramBuilder.DigitizeValue(length, suspension.TravelBins.AsSpan())] += 1;
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
        TelemetryTimeSelection? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var strokes = GetIncludedStrokes(telemetryData, suspension, strokeKind, range);

        var bins = CreateStrokeSpeedHistogramBins(strokes);
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

    private static double[] CreateStrokeSpeedHistogramBins(ImmutableArray<Stroke> strokes)
    {
        var maxSpeed = strokes.Length == 0
            ? Parameters.VelocityHistStep
            : strokes.Select(stroke => Math.Abs(stroke.Stat.MaxVelocity)).Max();
        var maxBin = Math.Max(
            Parameters.VelocityHistStep,
            Math.Ceiling(maxSpeed / Parameters.VelocityHistStep) * Parameters.VelocityHistStep);
        return HistogramBuilder.Linspace(0, maxBin, (int)(maxBin / Parameters.VelocityHistStep) + 1);
    }

    public static HistogramData CalculateDeepTravelHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeSelection? range = null)
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

            hist[HistogramBuilder.DigitizeValue(stroke.Stat.MaxTravel, bins.AsSpan())] += 1;
        }

        return new HistogramData(bins.ToList(), [.. hist]);
    }

    public static TravelStatistics CalculateTravelStatistics(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeSelection? range = null)
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
            TravelDistributionMode.DynamicSag => CalculateDynamicSagTravelStatistics(telemetryData, suspension, options.Selection),
            TravelDistributionMode.ActiveSuspension => CalculateActiveSuspensionTravelStatistics(telemetryData, suspension, options.Selection),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.HistogramMode, null),
        };
    }

    private static HistogramData CalculateActiveSuspensionTravelHistogram(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeSelection? range,
        StrokeCoarseIndexes coarseIndexes)
    {
        var histogram = new double[suspension.TravelBins.Length - 1];
        var totalCount = 0;

        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range).Concat(GetIncludedRebounds(telemetryData, suspension, range)))
        {
            var bins = coarseIndexes.Get(suspension, stroke).Travel.Span;
            for (var i = 0; i < bins.Length; i++)
            {
                if (!IncludesSample(range, stroke, i, telemetryData.Metadata.SampleRate)) continue;
                histogram[bins[i]]++;
                totalCount++;
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
        TelemetryTimeSelection? range)
    {
        var histogram = new double[suspension.TravelBins.Length - 1];
        var travelSamples = GetTravelSamples(telemetryData, suspension, range);

        foreach (var travel in travelSamples)
        {
            histogram[HistogramBuilder.DigitizeValue(travel, suspension.TravelBins.AsSpan())] += 1;
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
        TelemetryTimeSelection? range)
    {
        var sum = 0.0;
        var count = 0.0;
        var max = 0.0;
        var bottomouts = 0;

        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range).Concat(GetIncludedRebounds(telemetryData, suspension, range)))
        {
            if (range is null || range.CoversSamples(StrokeStartSeconds(stroke, telemetryData.Metadata.SampleRate),
                    StrokeEndSeconds(stroke, telemetryData.Metadata.SampleRate)))
            {
                sum += stroke.Stat.SumTravel;
                count += stroke.Stat.Count;
            }
            else
            {
                for (var i = stroke.Start; i <= stroke.End; i++)
                {
                    if (!IncludesSample(range, stroke, i - stroke.Start, telemetryData.Metadata.SampleRate)) continue;
                    sum += suspension.Travel[i];
                    count++;
                }
            }
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
        TelemetryTimeSelection? range)
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

    private static int CountBottomouts(ImmutableArray<double> travelSamples, double? maxTravel)
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
