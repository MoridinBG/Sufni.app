using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static StackedHistogramData CalculateVelocityHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        return CalculateVelocityHistogram(telemetryData, type, new VelocityStatisticsOptions(range));
    }

    public static StackedHistogramData CalculateVelocityHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        VelocityStatisticsOptions options)
    {
        var suspension = GetSuspension(telemetryData, type);

        return options.VelocityAverageMode switch
        {
            VelocityAverageMode.SampleAveraged => CalculateSampleVelocityHistogram(telemetryData, suspension, options.Range),
            VelocityAverageMode.StrokePeakAveraged => CalculateStrokePeakVelocityHistogram(telemetryData, suspension, options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.VelocityAverageMode, null),
        };
    }

    public static NormalDistributionData CalculateNormalDistribution(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var step = suspension.VelocityBins[1] - suspension.VelocityBins[0];
        var velocity = suspension.Velocity.ToList();

        var strokeVelocity = new List<double>();
        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range))
        {
            strokeVelocity.AddRange(velocity.GetRange(stroke.Start, stroke.End - stroke.Start + 1));
        }
        foreach (var stroke in GetIncludedRebounds(telemetryData, suspension, range))
        {
            strokeVelocity.AddRange(velocity.GetRange(stroke.Start, stroke.End - stroke.Start + 1));
        }

        if (strokeVelocity.Count < 2)
        {
            return new NormalDistributionData([], []);
        }

        var mu = strokeVelocity.Mean();
        var std = strokeVelocity.StandardDeviation();

        var min = strokeVelocity.Min();
        var max = strokeVelocity.Max();
        var velocityRange = max - min;
        var y = new double[100];
        for (var index = 0; index < 100; index++)
        {
            y[index] = min + index * velocityRange / 99;
        }

        var pdf = new List<double>(100);
        for (var index = 0; index < 100; index++)
        {
            pdf.Add(Normal.PDF(mu, std, y[index]) * step * 100);
        }

        return new NormalDistributionData([.. y], pdf);
    }

    public static VelocityStatistics CalculateVelocityStatistics(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        return CalculateVelocityStatistics(telemetryData, type, new VelocityStatisticsOptions(range));
    }

    public static VelocityStatistics CalculateVelocityStatistics(
        TelemetryData telemetryData,
        SuspensionType type,
        VelocityStatisticsOptions options)
    {
        var suspension = GetSuspension(telemetryData, type);
        var compressions = GetIncludedCompressions(telemetryData, suspension, options.Range);
        var rebounds = GetIncludedRebounds(telemetryData, suspension, options.Range);

        var averageCompression = options.VelocityAverageMode switch
        {
            VelocityAverageMode.SampleAveraged => CalculateSampleAverageVelocity(compressions),
            VelocityAverageMode.StrokePeakAveraged => CalculateStrokePeakAverageVelocity(compressions, signedRebound: false),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.VelocityAverageMode, null),
        };

        var averageRebound = options.VelocityAverageMode switch
        {
            VelocityAverageMode.SampleAveraged => CalculateSampleAverageVelocity(rebounds),
            VelocityAverageMode.StrokePeakAveraged => CalculateStrokePeakAverageVelocity(rebounds, signedRebound: true),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.VelocityAverageMode, null),
        };

        return new VelocityStatistics(
            averageRebound,
            CalculateMaxReboundVelocity(rebounds),
            averageCompression,
            CalculateMaxCompressionVelocity(compressions),
            CalculatePercentile95(rebounds, signedRebound: true),
            CalculatePercentile95(compressions, signedRebound: false),
            rebounds.Length,
            compressions.Length);
    }

    public static VelocityBands CalculateVelocityBands(
        TelemetryData telemetryData,
        SuspensionType type,
        double highSpeedThreshold,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var velocity = suspension.Velocity;

        var totalCount = 0.0;
        var lowSpeedCompression = 0.0;
        var highSpeedCompression = 0.0;

        foreach (var compression in GetIncludedCompressions(telemetryData, suspension, range))
        {
            totalCount += compression.Stat.Count;
            for (var index = compression.Start; index <= compression.End; index++)
            {
                if (velocity[index] < highSpeedThreshold)
                {
                    lowSpeedCompression++;
                }
                else
                {
                    highSpeedCompression++;
                }
            }
        }

        var lowSpeedRebound = 0.0;
        var highSpeedRebound = 0.0;

        foreach (var rebound in GetIncludedRebounds(telemetryData, suspension, range))
        {
            totalCount += rebound.Stat.Count;
            for (var index = rebound.Start; index <= rebound.End; index++)
            {
                if (velocity[index] > -highSpeedThreshold)
                {
                    lowSpeedRebound++;
                }
                else
                {
                    highSpeedRebound++;
                }
            }
        }

        if (totalCount <= 0)
        {
            return new VelocityBands(0, 0, 0, 0);
        }

        var totalPercentage = 100.0 / totalCount;
        return new VelocityBands(
            lowSpeedCompression * totalPercentage,
            highSpeedCompression * totalPercentage,
            lowSpeedRebound * totalPercentage,
            highSpeedRebound * totalPercentage);
    }

    public static bool HasStrokeData(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        if (!suspension.Present || suspension.Strokes is null)
        {
            return false;
        }

        return GetIncludedCompressions(telemetryData, suspension, range).Length > 0 ||
            GetIncludedRebounds(telemetryData, suspension, range).Length > 0;
    }

    private static StackedHistogramData CalculateSampleVelocityHistogram(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var divider = (suspension.TravelBins.Length - 1) / TelemetryData.TravelBinsForVelocityHistogram;
        var histogram = new double[suspension.VelocityBins.Length - 1][];
        for (var index = 0; index < histogram.Length; index++)
        {
            histogram[index] = Generate.Repeat<double>(TelemetryData.TravelBinsForVelocityHistogram, 0);
        }

        var totalCount = 0;
        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range).Concat(GetIncludedRebounds(telemetryData, suspension, range)))
        {
            totalCount += stroke.Stat.Count;
            for (var index = 0; index < stroke.Stat.Count; index++)
            {
                var velocityBin = stroke.DigitizedVelocity[index];
                var travelBin = stroke.DigitizedTravel[index] / divider;
                histogram[velocityBin][travelBin] += 1;
            }
        }

        if (totalCount <= 0)
        {
            return new StackedHistogramData(suspension.VelocityBins.ToList(), [.. histogram]);
        }

        foreach (var travelHistogram in histogram)
        {
            for (var index = 0; index < TelemetryData.TravelBinsForVelocityHistogram; index++)
            {
                travelHistogram[index] = travelHistogram[index] / totalCount * 100.0;
            }
        }

        return new StackedHistogramData(suspension.VelocityBins.ToList(), [.. histogram]);
    }

    private static StackedHistogramData CalculateStrokePeakVelocityHistogram(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var divider = (suspension.TravelBins.Length - 1) / TelemetryData.TravelBinsForVelocityHistogram;
        var histogram = new double[suspension.VelocityBins.Length - 1][];
        for (var index = 0; index < histogram.Length; index++)
        {
            histogram[index] = Generate.Repeat<double>(TelemetryData.TravelBinsForVelocityHistogram, 0);
        }

        var totalCount = 0;
        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range).Concat(GetIncludedRebounds(telemetryData, suspension, range)))
        {
            var velocityBin = HistogramBuilder.DigitizeValue(stroke.Stat.MaxVelocity, suspension.VelocityBins);
            var travelBin = HistogramBuilder.DigitizeValue(stroke.Stat.MaxTravel, suspension.TravelBins) / divider;
            histogram[velocityBin][Math.Clamp(travelBin, 0, TelemetryData.TravelBinsForVelocityHistogram - 1)] += 1;
            totalCount += 1;
        }

        if (totalCount <= 0)
        {
            return new StackedHistogramData(suspension.VelocityBins.ToList(), [.. histogram]);
        }

        foreach (var travelHistogram in histogram)
        {
            for (var index = 0; index < TelemetryData.TravelBinsForVelocityHistogram; index++)
            {
                travelHistogram[index] = travelHistogram[index] / totalCount * 100.0;
            }
        }

        return new StackedHistogramData(suspension.VelocityBins.ToList(), [.. histogram]);
    }

    private static double CalculateSampleAverageVelocity(Stroke[] strokes)
    {
        var sum = 0.0;
        var count = 0.0;
        foreach (var stroke in strokes)
        {
            sum += stroke.Stat.SumVelocity;
            count += stroke.Stat.Count;
        }

        return count > 0 ? sum / count : 0;
    }

    private static double CalculateStrokePeakAverageVelocity(Stroke[] strokes, bool signedRebound)
    {
        if (strokes.Length == 0)
        {
            return 0;
        }

        var average = strokes.Average(stroke => Math.Abs(stroke.Stat.MaxVelocity));
        return signedRebound ? -average : average;
    }

    private static double CalculateMaxCompressionVelocity(Stroke[] compressions)
    {
        return compressions.Length == 0 ? 0 : compressions.Max(stroke => stroke.Stat.MaxVelocity);
    }

    private static double CalculateMaxReboundVelocity(Stroke[] rebounds)
    {
        return rebounds.Length == 0 ? 0 : rebounds.Min(stroke => stroke.Stat.MaxVelocity);
    }

    private static double CalculatePercentile95(Stroke[] strokes, bool signedRebound)
    {
        if (strokes.Length == 0)
        {
            return 0;
        }

        var peakSpeeds = strokes
            .Select(stroke => Math.Abs(stroke.Stat.MaxVelocity))
            .Order()
            .ToArray();
        var index = Math.Clamp((int)Math.Ceiling(0.95 * peakSpeeds.Length) - 1, 0, peakSpeeds.Length - 1);
        var percentile = peakSpeeds[index];
        return signedRebound ? -percentile : percentile;
    }
}
