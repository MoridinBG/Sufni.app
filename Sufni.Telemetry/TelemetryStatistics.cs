using System.Diagnostics;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Statistics;

namespace Sufni.Telemetry;

public static class TelemetryStatistics
{
    private enum StrokeKind
    {
        Other,
        Compression,
        Rebound,
    }

    private readonly record struct SampleRange(int Start, int End)
    {
        public int Count => End - Start + 1;

        public bool Contains(Stroke stroke)
        {
            return stroke.Start >= Start && stroke.End <= End;
        }
    }

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

    public static bool HasVibrationData(TelemetryData telemetryData, ImuLocation location)
    {
        return telemetryData.ImuData is { Records.Count: > 0, ActiveLocations.Count: > 0 } &&
            telemetryData.ImuData.ActiveLocations.Contains((byte)location);
    }

    public static VibrationStats? CalculateVibration(
        TelemetryData telemetryData,
        ImuLocation location,
        SuspensionType pairedSuspension,
        TelemetryTimeRange? range = null)
    {
        if (!HasVibrationData(telemetryData, location) || !HasStrokeData(telemetryData, pairedSuspension, range))
        {
            return null;
        }

        Debug.Assert(telemetryData.ImuData is not null);
        var suspension = GetSuspension(telemetryData, pairedSuspension);
        if (suspension.MaxTravel is not > 0 ||
            suspension.Travel.Length < 2 ||
            telemetryData.Metadata.SampleRate <= 0 ||
            telemetryData.ImuData.SampleRate <= 0)
        {
            return null;
        }

        var meta = telemetryData.ImuData.Meta.FirstOrDefault(entry => entry.LocationId == (byte)location);
        if (meta is null || meta.AccelLsbPerG <= 0)
        {
            return null;
        }

        if (!TryGetSampleRange(telemetryData, suspension, range, out var sampleRange) || sampleRange.Count < 2)
        {
            return null;
        }

        var selectedStartSeconds = range is null ? 0.0 : sampleRange.Start / (double)telemetryData.Metadata.SampleRate;
        var selectedEndSeconds = range is null
            ? double.PositiveInfinity
            : (sampleRange.End + 1) / (double)telemetryData.Metadata.SampleRate;

        var accelUp = new List<double>();
        var locationCount = telemetryData.ImuData.ActiveLocations.Count;
        for (var index = 0; index < telemetryData.ImuData.Records.Count; index++)
        {
            var locationIndex = index % locationCount;
            if (telemetryData.ImuData.ActiveLocations[locationIndex] != (byte)location)
            {
                continue;
            }

            var record = telemetryData.ImuData.Records[index];
            accelUp.Add(Math.Abs(record.Az / (double)meta.AccelLsbPerG - 1.0));
        }

        if (accelUp.Count == 0)
        {
            return null;
        }

        var totalMovement = 0.0;
        for (var index = sampleRange.Start + 1; index <= sampleRange.End; index++)
        {
            totalMovement += Math.Abs(suspension.Travel[index] - suspension.Travel[index - 1]);
        }

        var strokeKinds = Enumerable.Repeat(StrokeKind.Other, suspension.Travel.Length).ToArray();
        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range))
        {
            var start = Math.Clamp(stroke.Start, 0, strokeKinds.Length - 1);
            var end = Math.Clamp(stroke.End, 0, strokeKinds.Length - 1);
            for (var index = start; index <= end; index++)
            {
                strokeKinds[index] = StrokeKind.Compression;
            }
        }

        foreach (var stroke in GetIncludedRebounds(telemetryData, suspension, range))
        {
            var start = Math.Clamp(stroke.Start, 0, strokeKinds.Length - 1);
            var end = Math.Clamp(stroke.End, 0, strokeKinds.Length - 1);
            for (var index = start; index <= end; index++)
            {
                strokeKinds[index] = StrokeKind.Rebound;
            }
        }

        var compression = new VibrationAccumulator();
        var rebound = new VibrationAccumulator();
        var other = new VibrationAccumulator();
        var overall = new VibrationAccumulator();

        for (var index = 0; index < accelUp.Count; index++)
        {
            var sampleTime = index / (double)telemetryData.ImuData.SampleRate;
            if (sampleTime < selectedStartSeconds || sampleTime >= selectedEndSeconds)
            {
                continue;
            }

            var suspensionIndex = Math.Clamp(
                (int)(sampleTime * telemetryData.Metadata.SampleRate),
                0,
                suspension.Travel.Length - 1);
            var positionRatio = suspension.Travel[suspensionIndex] / suspension.MaxTravel.Value;
            var g = accelUp[index];
            var kind = strokeKinds[suspensionIndex];

            overall.Add(g, positionRatio);
            switch (kind)
            {
                case StrokeKind.Compression:
                    compression.Add(g, positionRatio);
                    break;
                case StrokeKind.Rebound:
                    rebound.Add(g, positionRatio);
                    break;
                default:
                    other.Add(g, positionRatio);
                    break;
            }
        }

        if (overall.SumG <= 0)
        {
            return null;
        }

        var totalGSeconds = overall.SumG / telemetryData.ImuData.SampleRate;
        return new VibrationStats(
            compression.SumG / overall.SumG * 100.0,
            rebound.SumG / overall.SumG * 100.0,
            other.SumG / overall.SumG * 100.0,
            totalMovement / totalGSeconds,
            compression.AverageG,
            rebound.AverageG,
            overall.AverageG,
            compression.Thirds,
            rebound.Thirds,
            overall.Thirds);
    }

    public static bool HasBalanceData(
        TelemetryData telemetryData,
        BalanceType type,
        TelemetryTimeRange? range = null)
    {
        return HasBalanceData(telemetryData, type, new BalanceStatisticsOptions(range));
    }

    public static bool HasBalanceData(
        TelemetryData telemetryData,
        BalanceType type,
        BalanceStatisticsOptions options)
    {
        if (!HasStrokeData(telemetryData, SuspensionType.Front, options.Range) ||
            !HasStrokeData(telemetryData, SuspensionType.Rear, options.Range))
        {
            return false;
        }

        var frontTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Front, type, options);
        var rearTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Rear, type, options);

        return frontTravelVelocity.Item1.Length >= 2 && rearTravelVelocity.Item1.Length >= 2;
    }

    public static BalanceData CalculateBalance(
        TelemetryData telemetryData,
        BalanceType type,
        TelemetryTimeRange? range = null)
    {
        return CalculateBalance(telemetryData, type, new BalanceStatisticsOptions(range));
    }

    public static BalanceData CalculateBalance(
        TelemetryData telemetryData,
        BalanceType type,
        BalanceStatisticsOptions options)
    {
        var frontTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Front, type, options);
        var rearTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Rear, type, options);

        if (frontTravelVelocity.Item1.Length == 0 || rearTravelVelocity.Item1.Length == 0)
        {
            return new BalanceData([], [], [], [], [], [], 0);
        }

        var frontTrendLine = FitLinearTrend(frontTravelVelocity.Item1, frontTravelVelocity.Item2);
        var rearTrendLine = FitLinearTrend(rearTravelVelocity.Item1, rearTravelVelocity.Item2);

        var frontTrend = frontTravelVelocity.Item1.Select(frontTrendLine.Predict).ToList();
        var rearTrend = rearTravelVelocity.Item1.Select(rearTrendLine.Predict).ToList();

        var pairedCount = Math.Min(frontTrend.Count, rearTrend.Count);
        var sum = frontTrend.Zip(rearTrend, (front, rear) => front - rear).Sum();
        var meanSignedDeviation = pairedCount == 0 ? 0 : sum / pairedCount;
        var signedSlopeDeltaPercent = CalculateSlopeDeltaPercent(frontTrendLine.Slope, rearTrendLine.Slope);

        return new BalanceData(
            [.. frontTravelVelocity.Item1],
            [.. frontTravelVelocity.Item2],
            frontTrend,
            [.. rearTravelVelocity.Item1],
            [.. rearTravelVelocity.Item2],
            rearTrend,
            meanSignedDeviation,
            frontTrendLine.Slope,
            rearTrendLine.Slope,
            signedSlopeDeltaPercent,
            Math.Abs(signedSlopeDeltaPercent));
    }

    public static HistogramData CalculateTravelFrequencyHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var travelSamples = GetTravelSamples(telemetryData, suspension, range);
        if (travelSamples.Length < 2 || telemetryData.Metadata.SampleRate <= 0)
        {
            return new HistogramData([], []);
        }

        var sum = 0.0;
        foreach (var travel in travelSamples)
        {
            sum += travel;
        }
        var mean = sum / travelSamples.Length;

        var count = Math.Max(20000, travelSamples.Length);
        var complexSignal = new Complex[count];

        for (var index = 0; index < travelSamples.Length; index++)
        {
            complexSignal[index] = new Complex(travelSamples[index] - mean, 0);
        }

        for (var index = travelSamples.Length; index < count; index++)
        {
            complexSignal[index] = Complex.Zero;
        }

        Fourier.Forward(complexSignal, FourierOptions.Matlab);

        var halfCount = count / 2 + 1;
        var frequencies = new List<double>(halfCount);
        var spectrum = new List<double>(halfCount);

        var tick = 1.0 / telemetryData.Metadata.SampleRate;

        for (var index = 0; index < halfCount; index++)
        {
            var frequency = index / (count * tick);
            if (frequency > 10)
            {
                break;
            }

            frequencies.Add(frequency);
            var value = complexSignal[index];
            spectrum.Add(value.Magnitude * value.Magnitude);
        }

        return new HistogramData(frequencies, spectrum);
    }

    private static Suspension GetSuspension(TelemetryData telemetryData, SuspensionType type)
    {
        return type == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
    }

    private static bool TryGetSampleRange(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range,
        out SampleRange sampleRange)
    {
        sampleRange = default;
        if (suspension.Travel.Length == 0 || telemetryData.Metadata.SampleRate <= 0)
        {
            return false;
        }

        if (range is null)
        {
            sampleRange = new SampleRange(0, suspension.Travel.Length - 1);
            return true;
        }

        var startSeconds = Math.Clamp(range.Value.StartSeconds, 0, telemetryData.Metadata.Duration);
        var endSeconds = Math.Clamp(range.Value.EndSeconds, 0, telemetryData.Metadata.Duration);
        if (endSeconds - startSeconds < TelemetryTimeRange.MinimumDurationSeconds)
        {
            return false;
        }

        var startIndex = (int)Math.Floor(startSeconds * telemetryData.Metadata.SampleRate);
        var endIndex = (int)Math.Ceiling(endSeconds * telemetryData.Metadata.SampleRate) - 1;
        startIndex = Math.Clamp(startIndex, 0, suspension.Travel.Length - 1);
        endIndex = Math.Clamp(endIndex, 0, suspension.Travel.Length - 1);
        if (endIndex < startIndex)
        {
            return false;
        }

        sampleRange = new SampleRange(startIndex, endIndex);
        return true;
    }

    private static double[] GetTravelSamples(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        if (!TryGetSampleRange(telemetryData, suspension, range, out var sampleRange))
        {
            return [];
        }

        return range is null
            ? suspension.Travel
            : suspension.Travel[sampleRange.Start..(sampleRange.End + 1)];
    }

    private static Stroke[] GetIncludedStrokes(
        TelemetryData telemetryData,
        Suspension suspension,
        Stroke[]? strokes,
        TelemetryTimeRange? range)
    {
        if (strokes is null || strokes.Length == 0 ||
            !TryGetSampleRange(telemetryData, suspension, range, out var sampleRange))
        {
            return [];
        }

        return range is null
            ? strokes
            : strokes.Where(sampleRange.Contains).ToArray();
    }

    private static Stroke[] GetIncludedCompressions(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        return GetIncludedStrokes(telemetryData, suspension, suspension.Strokes?.Compressions, range);
    }

    private static Stroke[] GetIncludedRebounds(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        return GetIncludedStrokes(telemetryData, suspension, suspension.Strokes?.Rebounds, range);
    }

    private static Stroke[] GetIncludedStrokes(
        TelemetryData telemetryData,
        Suspension suspension,
        BalanceType strokeKind,
        TelemetryTimeRange? range)
    {
        return strokeKind == BalanceType.Compression
            ? GetIncludedCompressions(telemetryData, suspension, range)
            : GetIncludedRebounds(telemetryData, suspension, range);
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

    private readonly record struct LinearTrend(double Slope, double Intercept)
    {
        public double Predict(double x) => Slope * x + Intercept;
    }

    private static LinearTrend FitLinearTrend(double[] x, double[] y)
    {
        if (x.Length == 0 || y.Length == 0)
        {
            return new LinearTrend(0, 0);
        }

        if (x.Length == 1 || y.Length == 1 || x.Distinct().Count() == 1)
        {
            return new LinearTrend(0, y.Average());
        }

        var coefficients = Fit.Polynomial(x, y, 1);
        return new LinearTrend(coefficients[1], coefficients[0]);
    }

    private static (double[], double[]) TravelVelocity(
        TelemetryData telemetryData,
        SuspensionType suspensionType,
        BalanceType balanceType,
        BalanceStatisticsOptions options)
    {
        Debug.Assert(suspensionType != SuspensionType.Front || telemetryData.Front.MaxTravel is not null);
        Debug.Assert(suspensionType != SuspensionType.Rear || telemetryData.Rear.MaxTravel is not null);

        var suspension = GetSuspension(telemetryData, suspensionType);
        var travelMax = suspensionType == SuspensionType.Front
            ? telemetryData.Front.MaxTravel!.Value
            : telemetryData.Rear.MaxTravel!.Value;
        var strokes = GetIncludedStrokes(telemetryData, suspension, balanceType, options.Range);

        var travelValues = new List<double>();
        var velocityValues = new List<double>();

        foreach (var stroke in strokes)
        {
            var travel = options.DisplacementMode switch
            {
                BalanceDisplacementMode.Travel => Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]),
                BalanceDisplacementMode.Zenith => stroke.Stat.MaxTravel,
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.DisplacementMode, null),
            };

            travelValues.Add(travel / travelMax * 100);
            velocityValues.Add(balanceType == BalanceType.Rebound ? -stroke.Stat.MaxVelocity : stroke.Stat.MaxVelocity);
        }

        var travelArray = travelValues.ToArray();
        var velocityArray = velocityValues.ToArray();

        Array.Sort(travelArray, velocityArray);

        return (travelArray, velocityArray);
    }

    private static double CalculateSlopeDeltaPercent(double frontSlope, double rearSlope)
    {
        var denominator = Math.Max(Math.Abs(frontSlope), Math.Abs(rearSlope));
        return denominator < 1e-9 ? 0 : (frontSlope - rearSlope) / denominator * 100.0;
    }

    private sealed class VibrationAccumulator
    {
        private readonly double[] thirds = new double[3];

        public int Count { get; private set; }
        public double SumG { get; private set; }
        public double AverageG => Count == 0 ? 0 : SumG / Count;

        public StrokeThirds Thirds
        {
            get
            {
                if (SumG <= 0)
                {
                    return new StrokeThirds(0, 0, 0);
                }

                return new StrokeThirds(
                    thirds[0] / SumG * 100.0,
                    thirds[1] / SumG * 100.0,
                    thirds[2] / SumG * 100.0);
            }
        }

        public void Add(double g, double positionRatio)
        {
            Count++;
            SumG += g;

            var third = positionRatio switch
            {
                < 1.0 / 3.0 => 0,
                < 2.0 / 3.0 => 1,
                _ => 2,
            };
            thirds[third] += g;
        }
    }
}
