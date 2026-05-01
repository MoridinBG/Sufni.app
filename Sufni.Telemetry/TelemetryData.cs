using System.Diagnostics;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Statistics;
using MessagePack;
using Serilog;

#pragma warning disable CS8618

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public class Metadata
{
    public string SourceName { get; set; }
    public int Version { get; set; }
    public int SampleRate { get; set; }
    public long Timestamp { get; set; }
    public double Duration { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class Airtime
{
    public double Start { get; set; }
    public double End { get; set; }
};

[MessagePackObject(keyAsPropertyName: true)]
public class Suspension
{
    public bool Present { get; set; }
    public double AnomalyRate { get; set; }
    public double? MaxTravel { get; set; }
    public double[] Travel { get; set; }
    public double[] Velocity { get; set; }
    public Strokes Strokes { get; set; }
    public double[] TravelBins { get; set; }
    public double[] VelocityBins { get; set; }
    public double[] FineVelocityBins { get; set; }
};

public record BikeData(
    double HeadAngle,
    double? FrontMaxTravel,
    double? RearMaxTravel,
    Func<ushort, double>? FrontMeasurementToTravel,
    Func<ushort, double>? RearMeasurementToTravel);
public record HistogramData(List<double> Bins, List<double> Values);
public record StackedHistogramData(List<double> Bins, List<double[]> Values);

public record TravelStatistics(double Max, double Average, int Bottomouts);

public record VelocityStatistics(
    double AverageRebound,
    double MaxRebound,
    double AverageCompression,
    double MaxCompression,
    double Percentile95Rebound = 0,
    double Percentile95Compression = 0,
    int ReboundStrokeCount = 0,
    int CompressionStrokeCount = 0);

public record NormalDistributionData(
    List<double> Y,
    List<double> Pdf);

public record VelocityBands(
    double LowSpeedCompression,
    double HighSpeedCompression,
    double LowSpeedRebound,
    double HighSpeedRebound);

public enum SuspensionType
{
    Front,
    Rear
}

public enum BalanceType
{
    Compression,
    Rebound
}

public enum ImuLocation
{
    Frame = 0,
    Fork = 1,
    Shock = 2
}

public record BalanceData(
    List<double> FrontTravel,
    List<double> FrontVelocity,
    List<double> FrontTrend,
    List<double> RearTravel,
    List<double> RearVelocity,
    List<double> RearTrend,
    double MeanSignedDeviation,
    double FrontSlope = 0,
    double RearSlope = 0,
    double SignedSlopeDeltaPercent = 0,
    double AbsoluteSlopeDeltaPercent = 0);

public record StrokeThirds(double Lower, double Middle, double Upper);

public record VibrationStats(
    double CompressionPercent,
    double ReboundPercent,
    double OtherPercent,
    double MagicCarpet,
    double AverageGCompression,
    double AverageGRebound,
    double AverageGOverall,
    StrokeThirds CompressionThirds,
    StrokeThirds ReboundThirds,
    StrokeThirds OverallThirds);

[MessagePackObject(keyAsPropertyName: true)]
public class TelemetryData
{
    private static readonly ILogger logger = Log.ForContext<TelemetryData>();

    public const int TravelBinsForVelocityHistogram = 10;

    private enum StrokeKind
    {
        Other,
        Compression,
        Rebound
    }

    private readonly record struct SampleRange(int Start, int End)
    {
        public int Count => End - Start + 1;

        public bool Contains(Stroke stroke)
        {
            return stroke.Start >= Start && stroke.End <= End;
        }
    }

    #region Public properties

    public Metadata Metadata { get; set; }
    public Suspension Front { get; set; }
    public Suspension Rear { get; set; }
    public Airtime[] Airtimes { get; set; }
    public MarkerData[] Markers { get; set; } = [];
    public RawImuData? ImuData { get; set; }
    public GpsRecord[]? GpsData { get; set; }
    [IgnoreMember] public byte[] BinaryForm => MessagePackSerializer.Serialize(this);

    #endregion

    #region Constructors / Initializers

    public TelemetryData() { }

    private TelemetryData(Metadata metadata, double? frontMaxTravel, double? rearMaxTravel)
    {
        Metadata = metadata;

        Front = new Suspension
        {
            MaxTravel = frontMaxTravel,
            Strokes = new Strokes()
        };

        Rear = new Suspension
        {
            MaxTravel = rearMaxTravel,
            Strokes = new Strokes()
        };
    }

    public static TelemetryData FromBinary(byte[]? data)
    {
        return MessagePackSerializer.Deserialize<TelemetryData>(data);
    }

    #endregion

    #region Private helpers for ProcessRecording

    private static double[] Linspace(double min, double max, int num)
    {
        var step = (max - min) / (num - 1);
        var bins = new double[num];

        for (var i = 0; i < num; i++)
        {
            bins[i] = min + step * i;
        }

        return bins;
    }

    private static int[] Digitize(double[] data, double[] bins)
    {
        var inds = new int[data.Length];
        // Linspace-built bins[^1] can differ from the intended max by 1-2 ULPs,
        // so a clamped sample equal to MaxTravel may digitize one slot past the
        // last histogram bin. Clamping keeps the index inside the histogram.
        var maxBinIndex = bins.Length - 2;
        for (var k = 0; k < data.Length; k++)
        {
            var i = Array.BinarySearch(bins, data[k]);
            if (i < 0) i = ~i;
            // If current value is not exactly a bin boundary, we subtract 1 to make
            // the digitized slice indexed from 0 instead of 1. We do the same if a
            // value would exceed existing bins.
            if (data[k] >= bins[^1] || Math.Abs(data[k] - bins[i]) > 0.0001)
            {
                i -= 1;
            }
            inds[k] = Math.Clamp(i, 0, maxBinIndex);
        }
        return inds;
    }

    private static int DigitizeHistogramValue(double value, double[] bins)
    {
        if (bins.Length < 2)
        {
            return 0;
        }

        if (value <= bins[0])
        {
            return 0;
        }

        var maxBinIndex = bins.Length - 2;
        if (value >= bins[^1])
        {
            return maxBinIndex;
        }

        var index = Array.BinarySearch(bins, value);
        if (index >= 0)
        {
            return Math.Clamp(index - 1, 0, maxBinIndex);
        }

        return Math.Clamp(~index - 1, 0, maxBinIndex);
    }

    private void CalculateAirTimes()
    {
        var airtimes = new List<Airtime>();

        if (Front.Present && Rear.Present)
        {
            foreach (var f in Front.Strokes.Idlings)
            {
                if (!f.AirCandidate) continue;
                foreach (var r in Rear.Strokes.Idlings)
                {
                    if (!r.AirCandidate || !f.Overlaps(r)) continue;
                    f.AirCandidate = false;
                    r.AirCandidate = false;

                    var at = new Airtime
                    {
                        Start = Math.Max(f.Start, r.Start) / (double)Metadata.SampleRate,
                        End = Math.Min(f.End, r.End) / (double)Metadata.SampleRate
                    };
                    airtimes.Add(at);
                    break;
                }
            }

            var maxMean = (Front.MaxTravel + Rear.MaxTravel) / 2.0;

            foreach (var f in Front.Strokes.Idlings)
            {
                if (!f.AirCandidate) continue;
                var fMean = Front.Travel[f.Start..(f.End + 1)].Mean();
                var rMean = Rear.Travel[f.Start..(f.End + 1)].Mean();

                if (!((fMean + rMean) / 2 <= maxMean * Parameters.AirtimeTravelMeanThresholdRatio)) continue;
                var at = new Airtime
                {
                    Start = f.Start / (double)Metadata.SampleRate,
                    End = f.End / (double)Metadata.SampleRate
                };
                airtimes.Add(at);
            }

            foreach (var r in Rear.Strokes.Idlings)
            {
                if (!r.AirCandidate) continue;
                var fMean = Front.Travel[r.Start..(r.End + 1)].Mean();
                var rMean = Rear.Travel[r.Start..(r.End + 1)].Mean();

                if (!((fMean + rMean) / 2 <= maxMean * Parameters.AirtimeTravelMeanThresholdRatio)) continue;
                var at = new Airtime
                {
                    Start = r.Start / (double)Metadata.SampleRate,
                    End = r.End / (double)Metadata.SampleRate
                };
                airtimes.Add(at);
            }
        }
        else if (Front.Present)
        {
            foreach (var f in Front.Strokes.Idlings)
            {
                if (!f.AirCandidate) continue;
                var at = new Airtime
                {
                    Start = f.Start / (double)Metadata.SampleRate,
                    End = f.End / (double)Metadata.SampleRate
                };
                airtimes.Add(at);
            }
        }
        else if (Rear.Present)
        {
            foreach (var r in Rear.Strokes.Idlings)
            {
                if (!r.AirCandidate) continue;
                var at = new Airtime
                {
                    Start = r.Start / (double)Metadata.SampleRate,
                    End = r.End / (double)Metadata.SampleRate
                };
                airtimes.Add(at);
            }
        }

        Airtimes = [.. airtimes];
    }

    private static (double[], int[]) DigitizeVelocity(double[] v, double step)
    {
        // Subtracting half bin ensures that 0 will be at the middle of one bin
        var mn = (Math.Floor(v.Min() / step) - 0.5) * step;
        // Adding 1.5 bins ensures that all values will fit in bins, and that the last bin fits the step boundary.
        var mx = (Math.Floor(v.Max() / step) + 1.5) * step;
        var bins = Linspace(mn, mx, (int)((mx - mn) / step) + 1);
        var data = Digitize(v, bins);
        return (bins, data);
    }

    private static void CalculateSuspension(Suspension suspension, ushort[] data, Func<ushort, double> measurementToTravel, int sampleRate, double[] time, SavitzkyGolay filter)
    {
        Debug.Assert(suspension.MaxTravel is not null);

        suspension.Travel = new double[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            // Travel might under/overshoot because of erroneous data acquisition or
            // incorrect calibration. Errors might occur mid-ride (e.g. broken electrical
            // connection due to vibration), so we don't error out, just cap travel.
            // Errors like these will be obvious on the graphs, and the affected regions
            // can be filtered by hand.
            var travel = measurementToTravel(data[i]);
            travel = Math.Clamp(travel, 0, suspension.MaxTravel.Value);
            suspension.Travel[i] = travel;
        }

        var tbins = Linspace(0, suspension.MaxTravel.Value, Parameters.TravelHistBins + 1);
        var dt = Digitize(suspension.Travel, tbins);
        suspension.TravelBins = tbins;

        var v = filter.Process(suspension.Travel, time);
        suspension.Velocity = v;
        var (vbins, dv) = DigitizeVelocity(v, Parameters.VelocityHistStep);
        suspension.VelocityBins = vbins;
        var (vbinsFine, dvFine) = DigitizeVelocity(v, Parameters.VelocityHistStepFine);
        suspension.FineVelocityBins = vbinsFine;

        var strokes = Strokes.FilterStrokes(v, suspension.Travel, suspension.MaxTravel.Value, sampleRate);
        suspension.Strokes.Categorize(strokes);
        if (suspension.Strokes.Compressions.Length == 0 && suspension.Strokes.Rebounds.Length == 0)
        {
            suspension.Present = false;
        }
        else
        {
            suspension.Strokes.Digitize(dt, dv, dvFine);
        }
    }

    private static SavitzkyGolay? CreateVelocityFilter(int recordCount)
    {
        if (recordCount < 5)
        {
            return null;
        }

        var windowSize = Math.Min(51, recordCount);
        if (windowSize % 2 == 0)
        {
            windowSize--;
        }

        return SavitzkyGolay.Create(windowSize, 1, 3);
    }

    private static double CalculateAnomalyRate(int anomalyCount, int sampleCount, int sampleRate)
    {
        return sampleCount == 0
            ? 0
            : (double)anomalyCount / sampleCount * sampleRate;
    }

    #endregion

    #region PSST conversion

    public static TelemetryData FromRecording(RawTelemetryData rawData, Metadata metadata, BikeData bikeData)
    {
        return FromRecording(rawData, metadata, bikeData, logLifecycle: true);
    }

    private static TelemetryData FromRecording(RawTelemetryData rawData, Metadata metadata, BikeData bikeData, bool logLifecycle)
    {
        if (logLifecycle)
        {
            logger.Verbose(
                "Starting telemetry processing for source {SourceName} with sample rate {SampleRate}, version {Version}, {FrontSampleCount} front samples, and {RearSampleCount} rear samples",
                metadata.SourceName,
                metadata.SampleRate,
                metadata.Version,
                rawData.Front.Length,
                rawData.Rear.Length);
        }

        var td = new TelemetryData(metadata, bikeData.FrontMaxTravel, bikeData.RearMaxTravel);
        td.Markers = rawData.Markers;
        td.ImuData = rawData.ImuData;
        td.GpsData = rawData.GpsData;

        // Evaluate front and rear input arrays
        var fc = rawData.Front.Length;
        var rc = rawData.Rear.Length;
        td.Front.Present = fc != 0;
        td.Rear.Present = rc != 0;
        if (!td.Front.Present && !td.Rear.Present)
        {
            if (logLifecycle)
            {
                logger.Verbose("Telemetry processing aborted because both suspension sample arrays were empty");
            }

            throw new Exception("Front and rear record arrays are empty!");
        }
        if (td.Front.Present && td.Rear.Present && fc != rc)
        {
            if (logLifecycle)
            {
                logger.Verbose(
                    "Telemetry processing aborted because front and rear sample counts differed: {FrontSampleCount} front and {RearSampleCount} rear",
                    fc,
                    rc);
            }

            throw new Exception("Front and rear record counts are not equal!");
        }

        // Create time array
        var recordCount = Math.Max(fc, rc);
        var time = new double[recordCount];
        for (var i = 0; i < time.Length; i++)
        {
            time[i] = 1.0 / td.Metadata.SampleRate * i;
        }

        // Create a velocity filter that matches the capture size. Live captures may be
        // shorter than a full SST import during early-session save or stats recompute.
        var filter = CreateVelocityFilter(recordCount);

        // Calculate telemetry data
        if (td.Front.Present)
        {
            Debug.Assert(bikeData.FrontMeasurementToTravel is not null);
            if (filter is not null)
            {
                CalculateSuspension(td.Front, rawData.Front, bikeData.FrontMeasurementToTravel, td.Metadata.SampleRate, time, filter);
            }
            else
            {
                td.Front.Present = false;
            }
            td.Front.AnomalyRate = rawData.FrontAnomalyRate;
        }
        if (td.Rear.Present)
        {
            Debug.Assert(bikeData.RearMeasurementToTravel is not null);
            if (filter is not null)
            {
                CalculateSuspension(td.Rear, rawData.Rear, bikeData.RearMeasurementToTravel, td.Metadata.SampleRate, time, filter);
            }
            else
            {
                td.Rear.Present = false;
            }
            td.Rear.AnomalyRate = rawData.RearAnomalyRate;
        }

        td.CalculateAirTimes();

        if (logLifecycle)
        {
            logger.Verbose(
                "Telemetry processing completed with front present {FrontPresent}, rear present {RearPresent}, {AirtimeCount} airtimes, {MarkerCount} markers, IMU present {HasImuData}, and GPS points {GpsPointCount}",
                td.Front.Present,
                td.Rear.Present,
                td.Airtimes.Length,
                td.Markers.Length,
                td.ImuData is not null,
                td.GpsData?.Length ?? 0);
        }

        return td;
    }


    public static TelemetryData FromLiveCapture(LiveTelemetryCapture capture)
    {
        var front = capture.FrontMeasurements.ToArray();
        var rear = capture.RearMeasurements.ToArray();

        var frontAnomalyCount = 0;
        var rearAnomalyCount = 0;

        if (front.Length > 0)
        {
            var fixedFront = SpikeElimination.EliminateSpikes(Array.ConvertAll(front, v => (int)v));
            front = fixedFront.fixedSignal;
            frontAnomalyCount = fixedFront.anomalyCount;
        }

        if (rear.Length > 0)
        {
            var fixedRear = SpikeElimination.EliminateSpikes(Array.ConvertAll(rear, v => (int)v));
            rear = fixedRear.fixedSignal;
            rearAnomalyCount = fixedRear.anomalyCount;
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = (ushort)Math.Clamp(capture.Metadata.SampleRate, 0, ushort.MaxValue),
            Timestamp = capture.Metadata.Timestamp,
            Front = front,
            Rear = rear,
            FrontAnomalyRate = CalculateAnomalyRate(frontAnomalyCount, front.Length, capture.Metadata.SampleRate),
            RearAnomalyRate = CalculateAnomalyRate(rearAnomalyCount, rear.Length, capture.Metadata.SampleRate),
            Markers = capture.Markers,
            ImuData = capture.ImuData,
            GpsData = capture.GpsData,
        };

        return FromRecording(rawData, capture.Metadata, capture.BikeData, logLifecycle: false);
    }

    #endregion

    #region Data calculations

    private Suspension GetSuspension(SuspensionType type)
    {
        return type == SuspensionType.Front ? Front : Rear;
    }

    private bool TryGetSampleRange(
        Suspension suspension,
        TelemetryTimeRange? range,
        out SampleRange sampleRange)
    {
        sampleRange = default;
        if (suspension.Travel.Length == 0 || Metadata.SampleRate <= 0)
        {
            return false;
        }

        if (range is null)
        {
            sampleRange = new SampleRange(0, suspension.Travel.Length - 1);
            return true;
        }

        var startSeconds = Math.Clamp(range.Value.StartSeconds, 0, Metadata.Duration);
        var endSeconds = Math.Clamp(range.Value.EndSeconds, 0, Metadata.Duration);
        if (endSeconds - startSeconds < TelemetryTimeRange.MinimumDurationSeconds)
        {
            return false;
        }

        var startIndex = (int)Math.Floor(startSeconds * Metadata.SampleRate);
        var endIndex = (int)Math.Ceiling(endSeconds * Metadata.SampleRate) - 1;
        startIndex = Math.Clamp(startIndex, 0, suspension.Travel.Length - 1);
        endIndex = Math.Clamp(endIndex, 0, suspension.Travel.Length - 1);
        if (endIndex < startIndex)
        {
            return false;
        }

        sampleRange = new SampleRange(startIndex, endIndex);
        return true;
    }

    private double[] GetTravelSamples(Suspension suspension, TelemetryTimeRange? range)
    {
        if (!TryGetSampleRange(suspension, range, out var sampleRange))
        {
            return [];
        }

        return range is null
            ? suspension.Travel
            : suspension.Travel[sampleRange.Start..(sampleRange.End + 1)];
    }

    private Stroke[] GetIncludedStrokes(
        Suspension suspension,
        Stroke[]? strokes,
        TelemetryTimeRange? range)
    {
        if (strokes is null || strokes.Length == 0 ||
            !TryGetSampleRange(suspension, range, out var sampleRange))
        {
            return [];
        }

        return range is null
            ? strokes
            : strokes.Where(sampleRange.Contains).ToArray();
    }

    private Stroke[] GetIncludedCompressions(Suspension suspension, TelemetryTimeRange? range)
    {
        return GetIncludedStrokes(suspension, suspension.Strokes?.Compressions, range);
    }

    private Stroke[] GetIncludedRebounds(Suspension suspension, TelemetryTimeRange? range)
    {
        return GetIncludedStrokes(suspension, suspension.Strokes?.Rebounds, range);
    }

    private Stroke[] GetIncludedStrokes(
        Suspension suspension,
        BalanceType strokeKind,
        TelemetryTimeRange? range)
    {
        return strokeKind == BalanceType.Compression
            ? GetIncludedCompressions(suspension, range)
            : GetIncludedRebounds(suspension, range);
    }

    public HistogramData CalculateTravelHistogram(SuspensionType type, TelemetryTimeRange? range = null) =>
        CalculateTravelHistogram(type, new TravelStatisticsOptions(range));

    public HistogramData CalculateTravelHistogram(SuspensionType type, TravelStatisticsOptions options)
    {
        var suspension = GetSuspension(type);

        return options.HistogramMode switch
        {
            TravelHistogramMode.DynamicSag => CalculateDynamicSagTravelHistogram(suspension, options.Range),
            TravelHistogramMode.ActiveSuspension => CalculateActiveSuspensionTravelHistogram(suspension, options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.HistogramMode, null),
        };
    }

    private HistogramData CalculateActiveSuspensionTravelHistogram(Suspension suspension, TelemetryTimeRange? range)
    {
        var hist = new double[suspension.TravelBins.Length - 1];
        var totalCount = 0;

        foreach (var s in GetIncludedCompressions(suspension, range).Concat(GetIncludedRebounds(suspension, range)))
        {
            totalCount += s.Stat.Count;
            foreach (var d in s.DigitizedTravel)
            {
                hist[d] += 1;
            }
        }

        if (totalCount <= 0)
        {
            return new HistogramData(
                suspension.TravelBins.ToList(),
                [.. hist]);
        }

        hist = hist.Select(value => value / totalCount * 100.0).ToArray();

        return new HistogramData(
            suspension.TravelBins.ToList(), [.. hist]);
    }

    private HistogramData CalculateDynamicSagTravelHistogram(Suspension suspension, TelemetryTimeRange? range)
    {
        var hist = new double[suspension.TravelBins.Length - 1];
        var travelSamples = GetTravelSamples(suspension, range);

        foreach (var travel in travelSamples)
        {
            hist[DigitizeHistogramValue(travel, suspension.TravelBins)] += 1;
        }

        if (travelSamples.Length > 0)
        {
            hist = hist.Select(value => value / travelSamples.Length * 100.0).ToArray();
        }

        return new HistogramData(suspension.TravelBins.ToList(), [.. hist]);
    }

    public HistogramData CalculateStrokeLengthHistogram(
        SuspensionType type,
        BalanceType strokeKind,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        var strokes = GetIncludedStrokes(suspension, strokeKind, range);

        var hist = new double[suspension.TravelBins.Length - 1];
        foreach (var stroke in strokes)
        {
            var length = Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]);
            hist[DigitizeHistogramValue(length, suspension.TravelBins)] += 1;
        }

        if (strokes.Length > 0)
        {
            hist = hist.Select(value => value / strokes.Length * 100.0).ToArray();
        }

        return new HistogramData(suspension.TravelBins.ToList(), [.. hist]);
    }

    public HistogramData CalculateStrokeSpeedHistogram(
        SuspensionType type,
        BalanceType strokeKind,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        var strokes = GetIncludedStrokes(suspension, strokeKind, range);

        var maxSpeed = strokes.Length == 0
            ? Parameters.VelocityHistStep
            : strokes.Select(stroke => Math.Abs(stroke.Stat.MaxVelocity)).Max();
        var maxBin = Math.Max(
            Parameters.VelocityHistStep,
            Math.Ceiling(maxSpeed / Parameters.VelocityHistStep) * Parameters.VelocityHistStep);
        var bins = Linspace(0, maxBin, (int)(maxBin / Parameters.VelocityHistStep) + 1);
        var hist = new double[bins.Length - 1];

        foreach (var stroke in strokes)
        {
            var speed = Math.Abs(stroke.Stat.MaxVelocity);
            hist[DigitizeHistogramValue(speed, bins)] += 1;
        }

        if (strokes.Length > 0)
        {
            hist = hist.Select(value => value / strokes.Length * 100.0).ToArray();
        }

        return new HistogramData(bins.ToList(), [.. hist]);
    }

    public HistogramData CalculateDeepTravelHistogram(SuspensionType type, TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        Debug.Assert(suspension.MaxTravel is not null);

        var bins = suspension.TravelBins[^6..];
        var hist = new double[bins.Length - 1];
        var threshold = Parameters.DeepTravelThresholdRatio * suspension.MaxTravel.Value;

        foreach (var stroke in GetIncludedCompressions(suspension, range))
        {
            if (stroke.Stat.MaxTravel < threshold)
            {
                continue;
            }

            hist[DigitizeHistogramValue(stroke.Stat.MaxTravel, bins)] += 1;
        }

        return new HistogramData(bins.ToList(), [.. hist]);
    }

    public StackedHistogramData CalculateVelocityHistogram(SuspensionType type, TelemetryTimeRange? range = null) =>
        CalculateVelocityHistogram(type, new VelocityStatisticsOptions(range));

    public StackedHistogramData CalculateVelocityHistogram(SuspensionType type, VelocityStatisticsOptions options)
    {
        var suspension = GetSuspension(type);

        return options.VelocityAverageMode switch
        {
            VelocityAverageMode.SampleAveraged => CalculateSampleVelocityHistogram(suspension, options.Range),
            VelocityAverageMode.StrokePeakAveraged => CalculateStrokePeakVelocityHistogram(suspension, options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.VelocityAverageMode, null),
        };
    }

    private StackedHistogramData CalculateSampleVelocityHistogram(Suspension suspension, TelemetryTimeRange? range)
    {
        var divider = (suspension.TravelBins.Length - 1) / TravelBinsForVelocityHistogram;
        var hist = new double[suspension.VelocityBins.Length - 1][];
        for (var i = 0; i < hist.Length; i++)
        {
            hist[i] = Generate.Repeat<double>(TravelBinsForVelocityHistogram, 0);
        }

        var totalCount = 0;
        foreach (var s in GetIncludedCompressions(suspension, range).Concat(GetIncludedRebounds(suspension, range)))
        {
            totalCount += s.Stat.Count;
            for (int i = 0; i < s.Stat.Count; ++i)
            {
                var vbin = s.DigitizedVelocity[i];
                var tbin = s.DigitizedTravel[i] / divider;
                hist[vbin][tbin] += 1;
            }
        }

        if (totalCount <= 0)
        {
            return new StackedHistogramData(
                suspension.VelocityBins.ToList(),
                [.. hist]);
        }

        var largestBin = 0.0;
        foreach (var travelHist in hist)
        {
            var travelSum = 0.0;
            for (var j = 0; j < TravelBinsForVelocityHistogram; j++)
            {
                travelHist[j] = travelHist[j] / totalCount * 100.0;
                travelSum += travelHist[j];
            }

            largestBin = Math.Max(travelSum, largestBin);
        }

        return new StackedHistogramData(
            suspension.VelocityBins.ToList(), [.. hist]);
    }

    private StackedHistogramData CalculateStrokePeakVelocityHistogram(Suspension suspension, TelemetryTimeRange? range)
    {
        var divider = (suspension.TravelBins.Length - 1) / TravelBinsForVelocityHistogram;
        var hist = new double[suspension.VelocityBins.Length - 1][];
        for (var i = 0; i < hist.Length; i++)
        {
            hist[i] = Generate.Repeat<double>(TravelBinsForVelocityHistogram, 0);
        }

        var totalCount = 0;
        foreach (var stroke in GetIncludedCompressions(suspension, range).Concat(GetIncludedRebounds(suspension, range)))
        {
            var velocityBin = DigitizeHistogramValue(stroke.Stat.MaxVelocity, suspension.VelocityBins);
            var travelBin = DigitizeHistogramValue(stroke.Stat.MaxTravel, suspension.TravelBins) / divider;
            hist[velocityBin][Math.Clamp(travelBin, 0, TravelBinsForVelocityHistogram - 1)] += 1;
            totalCount += 1;
        }

        if (totalCount <= 0)
        {
            return new StackedHistogramData(suspension.VelocityBins.ToList(), [.. hist]);
        }

        foreach (var travelHist in hist)
        {
            for (var j = 0; j < TravelBinsForVelocityHistogram; j++)
            {
                travelHist[j] = travelHist[j] / totalCount * 100.0;
            }
        }

        return new StackedHistogramData(suspension.VelocityBins.ToList(), [.. hist]);
    }

    public NormalDistributionData CalculateNormalDistribution(SuspensionType type, TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        var step = suspension.VelocityBins[1] - suspension.VelocityBins[0];
        var velocity = suspension.Velocity.ToList();

        var strokeVelocity = new List<double>();
        foreach (var s in GetIncludedCompressions(suspension, range))
        {
            strokeVelocity.AddRange(velocity.GetRange(s.Start, s.End - s.Start + 1));
        }
        foreach (var s in GetIncludedRebounds(suspension, range))
        {
            strokeVelocity.AddRange(velocity.GetRange(s.Start, s.End - s.Start + 1));
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
        var ny = new double[100];
        for (int i = 0; i < 100; i++)
        {
            ny[i] = min + i * velocityRange / 99;
        }

        var pdf = new List<double>(100);
        for (int i = 0; i < 100; i++)
        {
            pdf.Add(Normal.PDF(mu, std, ny[i]) * step * 100);
        }

        return new NormalDistributionData([.. ny], pdf);
    }

    public TravelStatistics CalculateTravelStatistics(SuspensionType type, TelemetryTimeRange? range = null) =>
        CalculateTravelStatistics(type, new TravelStatisticsOptions(range));

    public TravelStatistics CalculateTravelStatistics(SuspensionType type, TravelStatisticsOptions options)
    {
        var suspension = GetSuspension(type);

        return options.HistogramMode switch
        {
            TravelHistogramMode.DynamicSag => CalculateDynamicSagTravelStatistics(suspension, options.Range),
            TravelHistogramMode.ActiveSuspension => CalculateActiveSuspensionTravelStatistics(suspension, options.Range),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.HistogramMode, null),
        };
    }

    private TravelStatistics CalculateActiveSuspensionTravelStatistics(Suspension suspension, TelemetryTimeRange? range)
    {
        var sum = 0.0;
        var count = 0.0;
        var mx = 0.0;
        var bo = 0;

        foreach (var stroke in GetIncludedCompressions(suspension, range).Concat(GetIncludedRebounds(suspension, range)))
        {
            sum += stroke.Stat.SumTravel;
            count += stroke.Stat.Count;
            bo += stroke.Stat.Bottomouts;
            if (stroke.Stat.MaxTravel > mx)
            {
                mx = stroke.Stat.MaxTravel;
            }
        }

        if (count <= 0)
        {
            return new TravelStatistics(0, 0, 0);
        }

        return new TravelStatistics(mx, sum / count, bo);
    }

    private TravelStatistics CalculateDynamicSagTravelStatistics(Suspension suspension, TelemetryTimeRange? range)
    {
        var travelSamples = GetTravelSamples(suspension, range);
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
        for (var i = 0; i < travelSamples.Length; i++)
        {
            if (!(travelSamples[i] > threshold)) continue;
            bottomouts += 1;
            for (; i < travelSamples.Length && travelSamples[i] > threshold; i++) { }
        }

        return bottomouts;
    }

    public VelocityStatistics CalculateVelocityStatistics(SuspensionType type, TelemetryTimeRange? range = null) =>
        CalculateVelocityStatistics(type, new VelocityStatisticsOptions(range));

    public VelocityStatistics CalculateVelocityStatistics(SuspensionType type, VelocityStatisticsOptions options)
    {
        var suspension = GetSuspension(type);
        var compressions = GetIncludedCompressions(suspension, options.Range);
        var rebounds = GetIncludedRebounds(suspension, options.Range);

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

    public VelocityBands CalculateVelocityBands(
        SuspensionType type,
        double highSpeedThreshold,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        var velocity = suspension.Velocity;

        var totalCount = 0.0;
        var lsc = 0.0;
        var hsc = 0.0;

        // Process compressions
        foreach (var compression in GetIncludedCompressions(suspension, range))
        {
            totalCount += compression.Stat.Count;
            for (int i = compression.Start; i <= compression.End; i++)
            {
                if (velocity[i] < highSpeedThreshold)
                {
                    lsc++;
                }
                else
                {
                    hsc++;
                }
            }
        }

        var lsr = 0.0;
        var hsr = 0.0;

        // Process rebounds
        foreach (var rebound in GetIncludedRebounds(suspension, range))
        {
            totalCount += rebound.Stat.Count;
            for (int i = rebound.Start; i <= rebound.End; i++)
            {
                if (velocity[i] > -highSpeedThreshold)
                {
                    lsr++;
                }
                else
                {
                    hsr++;
                }
            }
        }

        if (totalCount <= 0)
        {
            return new VelocityBands(0, 0, 0, 0);
        }

        var totalPercentage = 100.0 / totalCount;
        return new VelocityBands(
            lsc * totalPercentage,
            hsc * totalPercentage,
            lsr * totalPercentage,
            hsr * totalPercentage);
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

    public bool HasStrokeData(SuspensionType type, TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        if (!suspension.Present || suspension.Strokes is null)
        {
            return false;
        }

        return GetIncludedCompressions(suspension, range).Length > 0 ||
            GetIncludedRebounds(suspension, range).Length > 0;
    }

    public bool HasVibrationData(ImuLocation location)
    {
        return ImuData is { Records.Count: > 0, ActiveLocations.Count: > 0 } &&
            ImuData.ActiveLocations.Contains((byte)location);
    }

    public VibrationStats? CalculateVibration(
        ImuLocation location,
        SuspensionType pairedSuspension,
        TelemetryTimeRange? range = null)
    {
        if (!HasVibrationData(location) || !HasStrokeData(pairedSuspension, range))
        {
            return null;
        }

        Debug.Assert(ImuData is not null);
        var suspension = GetSuspension(pairedSuspension);
        if (suspension.MaxTravel is not > 0 ||
            suspension.Travel.Length < 2 ||
            Metadata.SampleRate <= 0 ||
            ImuData.SampleRate <= 0)
        {
            return null;
        }

        var meta = ImuData.Meta.FirstOrDefault(entry => entry.LocationId == (byte)location);
        if (meta is null || meta.AccelLsbPerG <= 0)
        {
            return null;
        }

        if (!TryGetSampleRange(suspension, range, out var sampleRange) || sampleRange.Count < 2)
        {
            return null;
        }

        var selectedStartSeconds = range is null ? 0.0 : sampleRange.Start / (double)Metadata.SampleRate;
        var selectedEndSeconds = range is null
            ? double.PositiveInfinity
            : (sampleRange.End + 1) / (double)Metadata.SampleRate;

        var accelUp = new List<double>();
        var locationCount = ImuData.ActiveLocations.Count;
        for (var i = 0; i < ImuData.Records.Count; i++)
        {
            var locationIndex = i % locationCount;
            if (ImuData.ActiveLocations[locationIndex] != (byte)location)
            {
                continue;
            }

            var record = ImuData.Records[i];
            accelUp.Add(Math.Abs(record.Az / (double)meta.AccelLsbPerG - 1.0));
        }

        if (accelUp.Count == 0)
        {
            return null;
        }

        var totalMovement = 0.0;
        for (var i = sampleRange.Start + 1; i <= sampleRange.End; i++)
        {
            totalMovement += Math.Abs(suspension.Travel[i] - suspension.Travel[i - 1]);
        }

        var strokeKinds = Enumerable.Repeat(StrokeKind.Other, suspension.Travel.Length).ToArray();
        foreach (var stroke in GetIncludedCompressions(suspension, range))
        {
            var start = Math.Clamp(stroke.Start, 0, strokeKinds.Length - 1);
            var end = Math.Clamp(stroke.End, 0, strokeKinds.Length - 1);
            for (var i = start; i <= end; i++)
            {
                strokeKinds[i] = StrokeKind.Compression;
            }
        }

        foreach (var stroke in GetIncludedRebounds(suspension, range))
        {
            var start = Math.Clamp(stroke.Start, 0, strokeKinds.Length - 1);
            var end = Math.Clamp(stroke.End, 0, strokeKinds.Length - 1);
            for (var i = start; i <= end; i++)
            {
                strokeKinds[i] = StrokeKind.Rebound;
            }
        }

        var compression = new VibrationAccumulator();
        var rebound = new VibrationAccumulator();
        var other = new VibrationAccumulator();
        var overall = new VibrationAccumulator();

        for (var i = 0; i < accelUp.Count; i++)
        {
            var sampleTime = i / (double)ImuData.SampleRate;
            if (sampleTime < selectedStartSeconds || sampleTime >= selectedEndSeconds)
            {
                continue;
            }

            var suspensionIndex = Math.Clamp(
                (int)(sampleTime * Metadata.SampleRate),
                0,
                suspension.Travel.Length - 1);
            var positionRatio = suspension.Travel[suspensionIndex] / suspension.MaxTravel.Value;
            var g = accelUp[i];
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

        // SumG scales with IMU sample count, so divide by the rate to get a g-second
        // integral that depends on the physical signal duration, not the IMU sample rate.
        var totalGSeconds = overall.SumG / ImuData.SampleRate;
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
                _ => 2
            };
            thirds[third] += g;
        }
    }

    private (double[], double[]) TravelVelocity(
        SuspensionType suspensionType,
        BalanceType balanceType,
        BalanceStatisticsOptions options)
    {
        Debug.Assert(suspensionType != SuspensionType.Front || Front.MaxTravel is not null);
        Debug.Assert(suspensionType != SuspensionType.Rear || Rear.MaxTravel is not null);

        var suspension = GetSuspension(suspensionType);
        var travelMax = suspensionType == SuspensionType.Front ? Front.MaxTravel!.Value : Rear.MaxTravel!.Value;
        var strokes = GetIncludedStrokes(suspension, balanceType, options.Range);

        var t = new List<double>();
        var v = new List<double>();

        foreach (var s in strokes)
        {
            var travel = options.DisplacementMode switch
            {
                BalanceDisplacementMode.Travel => Math.Abs(suspension.Travel[s.End] - suspension.Travel[s.Start]),
                BalanceDisplacementMode.Zenith => s.Stat.MaxTravel,
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.DisplacementMode, null),
            };

            t.Add(travel / travelMax * 100);

            // Use positive values for rebound too, because ScottPlot can't invert axis easily. 
            v.Add(balanceType == BalanceType.Rebound ? -s.Stat.MaxVelocity : s.Stat.MaxVelocity);
        }

        var tArray = t.ToArray();
        var vArray = v.ToArray();

        Array.Sort(tArray, vArray);

        return (tArray, vArray);
    }

    public bool HasBalanceData(BalanceType type, TelemetryTimeRange? range = null) =>
        HasBalanceData(type, new BalanceStatisticsOptions(range));

    public bool HasBalanceData(BalanceType type, BalanceStatisticsOptions options)
    {
        if (!HasStrokeData(SuspensionType.Front, options.Range) || !HasStrokeData(SuspensionType.Rear, options.Range))
        {
            return false;
        }

        var frontTravelVelocity = TravelVelocity(SuspensionType.Front, type, options);
        var rearTravelVelocity = TravelVelocity(SuspensionType.Rear, type, options);

        return frontTravelVelocity.Item1.Length >= 2 && rearTravelVelocity.Item1.Length >= 2;
    }

    public BalanceData CalculateBalance(BalanceType type, TelemetryTimeRange? range = null) =>
        CalculateBalance(type, new BalanceStatisticsOptions(range));

    public BalanceData CalculateBalance(BalanceType type, BalanceStatisticsOptions options)
    {
        var frontTravelVelocity = TravelVelocity(SuspensionType.Front, type, options);
        var rearTravelVelocity = TravelVelocity(SuspensionType.Rear, type, options);

        if (frontTravelVelocity.Item1.Length == 0 || rearTravelVelocity.Item1.Length == 0)
        {
            return new BalanceData([], [], [], [], [], [], 0);
        }

        var frontTrendLine = FitLinearTrend(frontTravelVelocity.Item1, frontTravelVelocity.Item2);
        var rearTrendLine = FitLinearTrend(rearTravelVelocity.Item1, rearTravelVelocity.Item2);

        var frontTrend = frontTravelVelocity.Item1.Select(frontTrendLine.Predict).ToList();
        var rearTrend = rearTravelVelocity.Item1.Select(rearTrendLine.Predict).ToList();

        var pairedCount = Math.Min(frontTrend.Count, rearTrend.Count);
        var sum = frontTrend.Zip(rearTrend, (fx, gx) => fx - gx).Sum();
        var msd = pairedCount == 0 ? 0 : sum / pairedCount;
        var signedSlopeDeltaPercent = CalculateSlopeDeltaPercent(frontTrendLine.Slope, rearTrendLine.Slope);

        return new BalanceData(
            [.. frontTravelVelocity.Item1],
            [.. frontTravelVelocity.Item2],
            frontTrend,
            [.. rearTravelVelocity.Item1],
            [.. rearTravelVelocity.Item2],
            rearTrend,
            msd,
            frontTrendLine.Slope,
            rearTrendLine.Slope,
            signedSlopeDeltaPercent,
            Math.Abs(signedSlopeDeltaPercent));
    }

    private static double CalculateSlopeDeltaPercent(double frontSlope, double rearSlope)
    {
        var denominator = Math.Max(Math.Abs(frontSlope), Math.Abs(rearSlope));
        return denominator < 1e-9 ? 0 : (frontSlope - rearSlope) / denominator * 100.0;
    }

    public HistogramData CalculateTravelFrequencyHistogram(SuspensionType type, TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(type);
        var travelSamples = GetTravelSamples(suspension, range);
        if (travelSamples.Length < 2 || Metadata.SampleRate <= 0)
        {
            return new HistogramData([], []);
        }

        // Calculate mean
        double sum = 0;
        foreach (var t in travelSamples)
        {
            sum += t;
        }
        var mean = sum / travelSamples.Length;

        // Determine final size (minimum 20000)
        var n = Math.Max(20000, travelSamples.Length);
        var complexSignal = new Complex[n];

        // Center travel data and pad
        for (var i = 0; i < travelSamples.Length; i++)
        {
            complexSignal[i] = new Complex(travelSamples[i] - mean, 0);
        }

        for (var i = travelSamples.Length; i < n; i++)
        {
            complexSignal[i] = Complex.Zero;
        }

        // Perform FFT
        Fourier.Forward(complexSignal, FourierOptions.Matlab);

        // Prepare output arrays
        var halfN = n / 2 + 1;
        var frequencies = new List<double>(halfN);
        var spectrum = new List<double>(halfN);

        var tick = 1.0 / Metadata.SampleRate;

        for (var i = 0; i < halfN; i++)
        {
            var freq = i / (n * tick);
            if (freq > 10) break;

            frequencies.Add(freq);
            var c = complexSignal[i];
            spectrum.Add(c.Magnitude * c.Magnitude);
        }

        return new HistogramData(frequencies, spectrum);
    }

    #endregion
}