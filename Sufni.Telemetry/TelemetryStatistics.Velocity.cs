using System.Runtime.CompilerServices;
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
        var compressions = GetIncludedCompressions(telemetryData, suspension, range);
        var rebounds = GetIncludedRebounds(telemetryData, suspension, range);
        var statistics = new NormalDistributionStatistics();
        AccumulateStrokeVelocity(suspension.Velocity, compressions, ref statistics);
        AccumulateStrokeVelocity(suspension.Velocity, rebounds, ref statistics);
        if (statistics.Count < 2)
        {
            return new NormalDistributionData([], []);
        }

        var mu = statistics.Mean;
        var std = statistics.StandardDeviation;
        var min = statistics.Minimum;
        var max = statistics.Maximum;
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

    private static void AccumulateStrokeVelocity(
        double[] velocity,
        Stroke[] strokes,
        ref NormalDistributionStatistics statistics)
    {
        foreach (var stroke in strokes)
        {
            for (var index = stroke.Start; index <= stroke.End; index++)
            {
                statistics.Add(velocity[index]);
            }
        }
    }

    private struct NormalDistributionStatistics
    {
        private double varianceAccumulator;
        private double varianceSum;

        public long Count { get; private set; }
        public double Mean { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public readonly double StandardDeviation => Math.Sqrt(varianceAccumulator / (Count - 1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(double value)
        {
            Count++;
            Mean += (value - Mean) / Count;

            if (Count == 1)
            {
                varianceSum = value;
                Minimum = value;
                Maximum = value;
                return;
            }

            varianceSum += value;
            var delta = Count * value - varianceSum;
            varianceAccumulator += delta * delta / (Count * (Count - 1));

            if (double.IsNaN(value))
            {
                Minimum = value;
                Maximum = value;
            }
            else
            {
                Minimum = Math.Min(Minimum, value);
                Maximum = Math.Max(Maximum, value);
            }
        }
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
        VelocityStatisticsOptions options)
    {
        var suspension = GetSuspension(telemetryData, type);
        return options.VelocityAverageMode switch
        {
            VelocityAverageMode.SampleAveraged => CalculateSampleVelocityBands(
                telemetryData,
                suspension,
                options.CompressionHighSpeedThreshold,
                options.ReboundHighSpeedThreshold,
                options.Range),
            VelocityAverageMode.StrokePeakAveraged => CalculateStrokePeakVelocityBands(
                telemetryData,
                suspension,
                options.CompressionHighSpeedThreshold,
                options.ReboundHighSpeedThreshold,
                options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.VelocityAverageMode, null),
        };
    }

    private static VelocityBands CalculateSampleVelocityBands(
        TelemetryData telemetryData,
        Suspension suspension,
        double compressionHighSpeedThreshold,
        double reboundHighSpeedThreshold,
        TelemetryTimeRange? range)
    {
        var velocity = suspension.Velocity;

        var totalCount = 0.0;
        var lowSpeedCompression = 0.0;
        var highSpeedCompression = 0.0;

        foreach (var compression in GetIncludedCompressions(telemetryData, suspension, range))
        {
            totalCount += compression.Stat.Count;
            for (var index = compression.Start; index <= compression.End; index++)
            {
                if (velocity[index] < compressionHighSpeedThreshold)
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
                if (velocity[index] > -reboundHighSpeedThreshold)
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

    private static VelocityBands CalculateStrokePeakVelocityBands(
        TelemetryData telemetryData,
        Suspension suspension,
        double compressionHighSpeedThreshold,
        double reboundHighSpeedThreshold,
        TelemetryTimeRange? range)
    {
        var compressions = GetIncludedCompressions(telemetryData, suspension, range);
        var rebounds = GetIncludedRebounds(telemetryData, suspension, range);
        var totalCount = compressions.Length + rebounds.Length;
        if (totalCount <= 0)
        {
            return new VelocityBands(0, 0, 0, 0);
        }

        var lowSpeedCompression = compressions.Count(stroke => stroke.Stat.MaxVelocity < compressionHighSpeedThreshold);
        var highSpeedCompression = compressions.Length - lowSpeedCompression;
        var lowSpeedRebound = rebounds.Count(stroke => stroke.Stat.MaxVelocity > -reboundHighSpeedThreshold);
        var highSpeedRebound = rebounds.Length - lowSpeedRebound;

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

    public static IReadOnlyList<TelemetryHighlightRange> CalculateHighlightRanges(
        TelemetryData telemetryData,
        TelemetryRangeSelection selection,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, selection.SuspensionType);
        if (!IsValidHighlightSelection(telemetryData, suspension))
        {
            return [];
        }

        var ranges = selection switch
        {
            DampingRangeSelection dampingSelection => CalculateVelocityStatisticsHighlightRanges(
                telemetryData,
                suspension,
                dampingSelection,
                range),
            StrokeLengthRangeSelection strokeLengthSelection => CalculateStrokeLengthHighlightRanges(
                telemetryData,
                suspension,
                strokeLengthSelection,
                range),
            StrokeSpeedRangeSelection strokeSpeedSelection => CalculateStrokeSpeedHighlightRanges(
                telemetryData,
                suspension,
                strokeSpeedSelection,
                range),
            DeepTravelRangeSelection deepTravelSelection => CalculateDeepTravelHighlightRanges(
                telemetryData,
                suspension,
                deepTravelSelection,
                range),
            _ => [],
        };

        return MergeHighlightRanges(ranges);
    }

    public static IReadOnlyList<TelemetryHighlightRange> MergeHighlightRanges(IEnumerable<TelemetryHighlightRange> ranges)
    {
        var merged = ranges
            .Where(range => range.EndSeconds > range.StartSeconds)
            .GroupBy(range => range.SuspensionType)
            .SelectMany(group => MergeOrderedHighlightRanges(group))
            .OrderBy(range => range.StartSeconds)
            .ThenBy(range => range.EndSeconds)
            .ThenBy(range => range.SuspensionType)
            .ToArray();

        return merged;
    }

    private static IReadOnlyList<TelemetryHighlightRange> MergeOrderedHighlightRanges(
        IEnumerable<TelemetryHighlightRange> ranges)
    {
        var ordered = ranges
            .OrderBy(range => range.StartSeconds)
            .ThenBy(range => range.EndSeconds)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var merged = new List<TelemetryHighlightRange> { ordered[0] };
        for (var index = 1; index < ordered.Length; index++)
        {
            var current = merged[^1];
            var next = ordered[index];
            if (next.StartSeconds <= current.EndSeconds)
            {
                merged[^1] = current with { EndSeconds = Math.Max(current.EndSeconds, next.EndSeconds) };
                continue;
            }

            merged.Add(next);
        }

        return merged;
    }

    private static StackedHistogramData CalculateSampleVelocityHistogram(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        var divider = GetVelocityHistogramTravelDivider(suspension);
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
        var divider = GetVelocityHistogramTravelDivider(suspension);
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

    private static bool IsValidHighlightSelection(
        TelemetryData telemetryData,
        Suspension suspension)
    {
        return telemetryData.Metadata?.SampleRate > 0 &&
            suspension.Present &&
            suspension.Strokes is not null;
    }

    private static IReadOnlyList<TelemetryHighlightRange> CalculateVelocityStatisticsHighlightRanges(
        TelemetryData telemetryData,
        Suspension suspension,
        DampingRangeSelection selection,
        TelemetryTimeRange? range)
    {
        if (!IsValidVelocityStatisticsSelection(suspension, selection))
        {
            return [];
        }

        var strokes = GetIncludedCompressions(telemetryData, suspension, range)
            .Concat(GetIncludedRebounds(telemetryData, suspension, range));
        var divider = GetVelocityHistogramTravelDivider(suspension);

        return selection.AverageMode switch
        {
            VelocityAverageMode.SampleAveraged => CalculateSampleVelocityStatisticsHighlightRanges(
                strokes,
                telemetryData.Metadata.SampleRate,
                selection,
                divider),
            VelocityAverageMode.StrokePeakAveraged => CalculateStrokePeakVelocityStatisticsHighlightRanges(
                strokes,
                suspension,
                telemetryData.Metadata.SampleRate,
                selection,
                divider),
            _ => [],
        };
    }

    private static bool IsValidVelocityStatisticsSelection(
        Suspension suspension,
        DampingRangeSelection selection)
    {
        return
            suspension.TravelBins?.Length > TelemetryData.TravelBinsForVelocityHistogram &&
            suspension.VelocityBins?.Length > 1 &&
            selection.VelocityBinIndex >= 0 &&
            selection.VelocityBinIndex < suspension.VelocityBins.Length - 1 &&
            selection.TravelBinStartIndex >= 0 &&
            selection.TravelBinEndIndex >= selection.TravelBinStartIndex &&
            selection.TravelBinEndIndex < TelemetryData.TravelBinsForVelocityHistogram &&
            GetVelocityHistogramTravelDivider(suspension) > 0;
    }

    private static int GetVelocityHistogramTravelDivider(Suspension suspension)
    {
        return (suspension.TravelBins.Length - 1) / TelemetryData.TravelBinsForVelocityHistogram;
    }

    private static IReadOnlyList<TelemetryHighlightRange> CalculateStrokeLengthHighlightRanges(
        TelemetryData telemetryData,
        Suspension suspension,
        StrokeLengthRangeSelection selection,
        TelemetryTimeRange? range)
    {
        if (suspension.TravelBins is not { Length: >= 2 } travelBins ||
            !selection.Bin.MatchesBins(travelBins))
        {
            return [];
        }

        var strokes = GetIncludedStrokes(telemetryData, suspension, selection.StrokeKind, range);
        var ranges = new List<TelemetryHighlightRange>();
        foreach (var stroke in strokes)
        {
            if (!IsValidStrokeSampleRange(suspension, stroke))
            {
                continue;
            }

            var length = Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]);
            if (HistogramBuilder.DigitizeValue(length, travelBins) == selection.Bin.Index)
            {
                ranges.Add(CreateHighlightRange(stroke, telemetryData.Metadata.SampleRate));
            }
        }

        return ranges;
    }

    private static IReadOnlyList<TelemetryHighlightRange> CalculateStrokeSpeedHighlightRanges(
        TelemetryData telemetryData,
        Suspension suspension,
        StrokeSpeedRangeSelection selection,
        TelemetryTimeRange? range)
    {
        var strokes = GetIncludedStrokes(telemetryData, suspension, selection.StrokeKind, range);
        var bins = CreateStrokeSpeedHistogramBins(strokes);
        if (!selection.Bin.MatchesBins(bins))
        {
            return [];
        }

        var ranges = new List<TelemetryHighlightRange>();
        foreach (var stroke in strokes)
        {
            var speed = Math.Abs(stroke.Stat.MaxVelocity);
            if (HistogramBuilder.DigitizeValue(speed, bins) == selection.Bin.Index)
            {
                ranges.Add(CreateHighlightRange(stroke, telemetryData.Metadata.SampleRate));
            }
        }

        return ranges;
    }

    private static IReadOnlyList<TelemetryHighlightRange> CalculateDeepTravelHighlightRanges(
        TelemetryData telemetryData,
        Suspension suspension,
        DeepTravelRangeSelection selection,
        TelemetryTimeRange? range)
    {
        if (suspension.MaxTravel is not { } maxTravel ||
            suspension.TravelBins is not { Length: >= 6 } travelBins)
        {
            return [];
        }

        var bins = travelBins[^6..];
        if (!selection.Bin.MatchesBins(bins))
        {
            return [];
        }

        var threshold = Parameters.DeepTravelThresholdRatio * maxTravel;
        var ranges = new List<TelemetryHighlightRange>();
        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range))
        {
            if (stroke.Stat.MaxTravel < threshold)
            {
                continue;
            }

            if (HistogramBuilder.DigitizeValue(stroke.Stat.MaxTravel, bins) == selection.Bin.Index)
            {
                ranges.Add(CreateHighlightRange(stroke, telemetryData.Metadata.SampleRate));
            }
        }

        return ranges;
    }

    private static bool IsValidStrokeSampleRange(Suspension suspension, Stroke stroke)
    {
        return stroke.Start >= 0 &&
               stroke.End >= stroke.Start &&
               stroke.End < suspension.Travel.Length;
    }

    private static List<TelemetryHighlightRange> CalculateSampleVelocityStatisticsHighlightRanges(
        IEnumerable<Stroke> strokes,
        int sampleRate,
        DampingRangeSelection selection,
        int divider)
    {
        var ranges = new List<TelemetryHighlightRange>();
        foreach (var stroke in strokes)
        {
            var matchingStart = -1;
            var sampleCount = Math.Min(stroke.Stat.Count, Math.Min(stroke.DigitizedVelocity.Length, stroke.DigitizedTravel.Length));
            for (var index = 0; index < sampleCount; index++)
            {
                var travelBin = stroke.DigitizedTravel[index] / divider;
                var matches = stroke.DigitizedVelocity[index] == selection.VelocityBinIndex &&
                    travelBin >= selection.TravelBinStartIndex &&
                    travelBin <= selection.TravelBinEndIndex;

                if (matches)
                {
                    matchingStart = matchingStart < 0 ? index : matchingStart;
                    continue;
                }

                if (matchingStart >= 0)
                {
                    ranges.Add(CreateHighlightRange(stroke, matchingStart, index - 1, sampleRate));
                    matchingStart = -1;
                }
            }

            if (matchingStart >= 0)
            {
                ranges.Add(CreateHighlightRange(stroke, matchingStart, sampleCount - 1, sampleRate));
            }
        }

        return ranges;
    }

    private static List<TelemetryHighlightRange> CalculateStrokePeakVelocityStatisticsHighlightRanges(
        IEnumerable<Stroke> strokes,
        Suspension suspension,
        int sampleRate,
        DampingRangeSelection selection,
        int divider)
    {
        var ranges = new List<TelemetryHighlightRange>();
        foreach (var stroke in strokes)
        {
            var velocityBin = HistogramBuilder.DigitizeValue(stroke.Stat.MaxVelocity, suspension.VelocityBins);
            var travelBin = HistogramBuilder.DigitizeValue(stroke.Stat.MaxTravel, suspension.TravelBins) / divider;
            travelBin = Math.Clamp(travelBin, 0, TelemetryData.TravelBinsForVelocityHistogram - 1);
            if (velocityBin != selection.VelocityBinIndex ||
                travelBin < selection.TravelBinStartIndex ||
                travelBin > selection.TravelBinEndIndex)
            {
                continue;
            }

            ranges.Add(CreateHighlightRange(stroke, sampleRate));
        }

        return ranges;
    }

    private static TelemetryHighlightRange CreateHighlightRange(Stroke stroke, int sampleRate)
    {
        var step = sampleRate > 0 ? 1.0 / sampleRate : 0.0;
        return new TelemetryHighlightRange(
            StrokeStartSeconds(stroke, sampleRate),
            StrokeEndSeconds(stroke, sampleRate) + step);
    }

    private static TelemetryHighlightRange CreateHighlightRange(
        Stroke stroke,
        int firstStrokeSampleOffset,
        int lastStrokeSampleOffset,
        int sampleRate)
    {
        var step = sampleRate > 0 ? 1.0 / sampleRate : 0.0;
        var strokeStartSeconds = StrokeStartSeconds(stroke, sampleRate);
        return new TelemetryHighlightRange(
            strokeStartSeconds + firstStrokeSampleOffset * step,
            strokeStartSeconds + (lastStrokeSampleOffset + 1) * step);
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
