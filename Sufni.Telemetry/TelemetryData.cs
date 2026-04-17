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
    public int Timestamp { get; set; }
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
    double MaxCompression);

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

public record BalanceData(
    List<double> FrontTravel,
    List<double> FrontVelocity,
    List<double> FrontTrend,
    List<double> RearTravel,
    List<double> RearVelocity,
    List<double> RearTrend,
    double MeanSignedDeviation);

[MessagePackObject(keyAsPropertyName: true)]
public class TelemetryData
{
    private static readonly ILogger logger = Log.ForContext<TelemetryData>();

    public const int TravelBinsForVelocityHistogram = 10;

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
            inds[k] = i;
        }
        return inds;
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
                        Start = Math.Min(f.Start, r.Start) / (double)Metadata.SampleRate,
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
            var fixedFront = SpikeElimination.EliminateSpikes(front.Select(value => (int)value).ToArray());
            front = fixedFront.fixedSignal;
            frontAnomalyCount = fixedFront.anomalyCount;
        }

        if (rear.Length > 0)
        {
            var fixedRear = SpikeElimination.EliminateSpikes(rear.Select(value => (int)value).ToArray());
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

    public HistogramData CalculateTravelHistogram(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;

        var hist = new double[suspension.TravelBins.Length - 1];
        var totalCount = 0;

        foreach (var s in suspension.Strokes.Compressions.Concat(suspension.Strokes.Rebounds))
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
                suspension.TravelBins.ToList().GetRange(0, suspension.TravelBins.Length),
                [.. hist]);
        }

        hist = hist.Select(value => value / totalCount * 100.0).ToArray();

        return new HistogramData(
            suspension.TravelBins.ToList().GetRange(0, suspension.TravelBins.Length), [.. hist]);
    }

    public StackedHistogramData CalculateVelocityHistogram(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;

        var divider = (suspension.TravelBins.Length - 1) / TravelBinsForVelocityHistogram;
        var hist = new double[suspension.VelocityBins.Length - 1][];
        for (var i = 0; i < hist.Length; i++)
        {
            hist[i] = Generate.Repeat<double>(TravelBinsForVelocityHistogram, 0);
        }

        var totalCount = 0;
        foreach (var s in suspension.Strokes.Compressions.Concat(suspension.Strokes.Rebounds))
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
                suspension.VelocityBins.ToList().GetRange(0, suspension.VelocityBins.Length),
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
            suspension.VelocityBins.ToList().GetRange(0, suspension.VelocityBins.Length), [.. hist]);
    }

    public NormalDistributionData CalculateNormalDistribution(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;
        var step = suspension.VelocityBins[1] - suspension.VelocityBins[0];
        var velocity = suspension.Velocity.ToList();

        var strokeVelocity = new List<double>();
        foreach (var s in suspension.Strokes.Compressions)
        {
            strokeVelocity.AddRange(velocity.GetRange(s.Start, s.End - s.Start + 1));
        }
        foreach (var s in suspension.Strokes.Rebounds)
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
        var range = max - min;
        var ny = new double[100];
        for (int i = 0; i < 100; i++)
        {
            ny[i] = min + i * range / 99;
        }

        var pdf = new List<double>(100);
        for (int i = 0; i < 100; i++)
        {
            pdf.Add(Normal.PDF(mu, std, ny[i]) * step * 100);
        }

        return new NormalDistributionData([.. ny], pdf);
    }

    public TravelStatistics CalculateTravelStatistics(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;

        var sum = 0.0;
        var count = 0.0;
        var mx = 0.0;
        var bo = 0;

        foreach (var stroke in suspension.Strokes.Compressions.Concat(suspension.Strokes.Rebounds))
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

    public VelocityStatistics CalculateVelocityStatistics(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;

        var csum = 0.0;
        var ccount = 0.0;
        var maxc = 0.0;
        foreach (var compression in suspension.Strokes.Compressions)
        {
            csum += compression.Stat.SumVelocity;
            ccount += compression.Stat.Count;
            if (compression.Stat.MaxVelocity > maxc)
            {
                maxc = compression.Stat.MaxVelocity;
            }
        }
        var rsum = 0.0;
        var rcount = 0.0;
        var maxr = 0.0;
        foreach (var rebound in suspension.Strokes.Rebounds)
        {
            rsum += rebound.Stat.SumVelocity;
            rcount += rebound.Stat.Count;
            if (rebound.Stat.MaxVelocity < maxr)
            {
                maxr = rebound.Stat.MaxVelocity;
            }
        }

        return new VelocityStatistics(
            rcount > 0 ? rsum / rcount : 0,
            maxr,
            ccount > 0 ? csum / ccount : 0,
            maxc);
    }

    public VelocityBands CalculateVelocityBands(SuspensionType type, double highSpeedThreshold)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;
        var velocity = suspension.Velocity;

        var totalCount = 0.0;
        var lsc = 0.0;
        var hsc = 0.0;

        // Process compressions
        foreach (var compression in suspension.Strokes.Compressions)
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
        foreach (var rebound in suspension.Strokes.Rebounds)
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

    private static Func<double, double> FitPolynomial(double[] x, double[] y)
    {
        if (x.Length == 0 || y.Length == 0)
        {
            return _ => 0;
        }

        if (x.Length == 1 || y.Length == 1)
        {
            return _ => y[0];
        }

        var coefficients = Fit.Polynomial(x, y, 1);
        return t => coefficients[1] * t + coefficients[0];
    }

    public bool HasStrokeData(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;
        if (!suspension.Present || suspension.Strokes is null)
        {
            return false;
        }

        return suspension.Strokes.Compressions.Length > 0 || suspension.Strokes.Rebounds.Length > 0;
    }

    private (double[], double[]) TravelVelocity(SuspensionType suspensionType, BalanceType balanceType)
    {
        Debug.Assert(suspensionType != SuspensionType.Front || Front.MaxTravel is not null);
        Debug.Assert(suspensionType != SuspensionType.Rear || Rear.MaxTravel is not null);

        var suspension = suspensionType == SuspensionType.Front ? Front : Rear;
        var travelMax = suspensionType == SuspensionType.Front ? Front.MaxTravel!.Value : Rear.MaxTravel!.Value;
        var strokes = balanceType == BalanceType.Compression
            ? suspension.Strokes.Compressions
            : suspension.Strokes.Rebounds;

        var t = new List<double>();
        var v = new List<double>();

        foreach (var s in strokes)
        {
            t.Add(s.Stat.MaxTravel / travelMax * 100);

            // Use positive values for rebound too, because ScottPlot can't invert axis easily. 
            v.Add(balanceType == BalanceType.Rebound ? -s.Stat.MaxVelocity : s.Stat.MaxVelocity);
        }

        var tArray = t.ToArray();
        var vArray = v.ToArray();

        Array.Sort(tArray, vArray);

        return (tArray, vArray);
    }

    public bool HasBalanceData(BalanceType type)
    {
        if (!HasStrokeData(SuspensionType.Front) || !HasStrokeData(SuspensionType.Rear))
        {
            return false;
        }

        var frontTravelVelocity = TravelVelocity(SuspensionType.Front, type);
        var rearTravelVelocity = TravelVelocity(SuspensionType.Rear, type);

        return frontTravelVelocity.Item1.Length >= 2 && rearTravelVelocity.Item1.Length >= 2;
    }

    public BalanceData CalculateBalance(BalanceType type)
    {
        var frontTravelVelocity = TravelVelocity(SuspensionType.Front, type);
        var rearTravelVelocity = TravelVelocity(SuspensionType.Rear, type);

        if (frontTravelVelocity.Item1.Length == 0 || rearTravelVelocity.Item1.Length == 0)
        {
            return new BalanceData([], [], [], [], [], [], 0);
        }

        var frontPoly = FitPolynomial(frontTravelVelocity.Item1, frontTravelVelocity.Item2);
        var rearPoly = FitPolynomial(rearTravelVelocity.Item1, rearTravelVelocity.Item2);

        var frontTrend = frontTravelVelocity.Item1.Select(t => frontPoly(t)).ToList();
        var rearTrend = rearTravelVelocity.Item1.Select(t => rearPoly(t)).ToList();

        var pairedCount = Math.Min(frontTrend.Count, rearTrend.Count);
        var sum = frontTrend.Zip(rearTrend, (fx, gx) => fx - gx).Sum();
        var msd = pairedCount == 0 ? 0 : sum / pairedCount;

        return new BalanceData(
            [.. frontTravelVelocity.Item1],
            [.. frontTravelVelocity.Item2],
            frontTravelVelocity.Item1.Select(t => frontPoly(t)).ToList(),
            [.. rearTravelVelocity.Item1],
            [.. rearTravelVelocity.Item2],
            rearTravelVelocity.Item1.Select(t => rearPoly(t)).ToList(),
            msd);
    }

    public HistogramData CalculateTravelFrequencyHistogram(SuspensionType type)
    {
        var suspension = type == SuspensionType.Front ? Front : Rear;

        // Calculate mean
        double sum = 0;
        foreach (var t in suspension.Travel)
        {
            sum += t;
        }
        var mean = sum / suspension.Travel.Length;

        // Determine final size (minimum 20000)
        var n = Math.Max(20000, suspension.Travel.Length);
        var complexSignal = new Complex[n];

        // Center travel data and pad
        for (var i = 0; i < suspension.Travel.Length; i++)
        {
            complexSignal[i] = new Complex(suspension.Travel[i] - mean, 0);
        }

        for (var i = suspension.Travel.Length; i < n; i++)
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