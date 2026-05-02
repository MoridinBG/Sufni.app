using System.Diagnostics;
using MathNet.Numerics.Statistics;
using MessagePack;
using Serilog;

#pragma warning disable CS8618

namespace Sufni.Telemetry;

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

    private static void ApplySuspensionTrace(Suspension suspension, ProcessedSuspensionTrace trace)
    {
        suspension.Present = trace.Present;
        suspension.Travel = trace.Travel;
        suspension.Velocity = trace.Velocity;
        suspension.Strokes = trace.Strokes;
        suspension.TravelBins = trace.TravelBins;
        suspension.VelocityBins = trace.VelocityBins;
        suspension.FineVelocityBins = trace.FineVelocityBins;
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
                var front = MeasurementPreprocessor.Process(rawData.Front, MeasurementPreprocessor.SensorTypeForWrapping(bikeData.FrontMeasurementWraps));
                var frontTrace = SuspensionTraceProcessor.Process(front.Samples, td.Front.MaxTravel!.Value, bikeData.FrontMeasurementToTravel, td.Metadata.SampleRate, time, filter);
                ApplySuspensionTrace(td.Front, frontTrace);
                td.Front.AnomalyRate = CalculateAnomalyRate(front.AnomalyCount, front.Samples.Length, td.Metadata.SampleRate);
            }
            else
            {
                td.Front.Present = false;
            }
        }
        if (td.Rear.Present)
        {
            Debug.Assert(bikeData.RearMeasurementToTravel is not null);
            if (filter is not null)
            {
                var rear = MeasurementPreprocessor.Process(rawData.Rear, MeasurementPreprocessor.SensorTypeForWrapping(bikeData.RearMeasurementWraps));
                var rearTrace = SuspensionTraceProcessor.Process(rear.Samples, td.Rear.MaxTravel!.Value, bikeData.RearMeasurementToTravel, td.Metadata.SampleRate, time, filter);
                ApplySuspensionTrace(td.Rear, rearTrace);
                td.Rear.AnomalyRate = CalculateAnomalyRate(rear.AnomalyCount, rear.Samples.Length, td.Metadata.SampleRate);
            }
            else
            {
                td.Rear.Present = false;
            }
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
        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = (ushort)Math.Clamp(capture.Metadata.SampleRate, 0, ushort.MaxValue),
            Timestamp = capture.Metadata.Timestamp,
            Front = capture.FrontMeasurements.ToArray(),
            Rear = capture.RearMeasurements.ToArray(),
            Markers = capture.Markers,
            ImuData = capture.ImuData,
            GpsData = capture.GpsData,
        };

        return FromRecording(rawData, capture.Metadata, capture.BikeData, logLifecycle: false);
    }

    #endregion
}