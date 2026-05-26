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
    public TemperatureAverage[] TemperatureAverages { get; set; } = [];
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

    private static void ProcessSuspensionSide(
        Suspension suspension,
        ushort[] rawSamples,
        bool measurementWraps,
        Func<ushort, double>? measurementToTravel,
        int sampleRate,
        double[] time,
        SavitzkyGolay? filter)
    {
        if (!suspension.Present)
        {
            return;
        }

        Debug.Assert(measurementToTravel is not null);
        if (measurementToTravel is null)
        {
            throw new InvalidOperationException("Present suspension is missing travel calibration.");
        }

        var preprocessed = MeasurementPreprocessor.Process(
            rawSamples,
            MeasurementPreprocessor.SensorTypeForWrapping(measurementWraps),
            sampleRate);
        var trace = SuspensionTraceProcessor.Process(
            preprocessed.Samples,
            suspension.MaxTravel!.Value,
            measurementToTravel,
            sampleRate,
            time,
            filter);

        ApplySuspensionTrace(suspension, trace);
        suspension.AnomalyRate = CalculateAnomalyRate(preprocessed.AnomalyCount, preprocessed.Samples.Length, sampleRate);
    }

    private static SavitzkyGolay? CreateVelocityFilter(
        int recordCount,
        int sampleRate,
        TelemetryProcessingOptions processingOptions)
    {
        if (!processingOptions.UsesVelocityFilter)
        {
            return null;
        }

        var target = (int)Math.Round(sampleRate * processingOptions.VelocityFilterWindowSeconds);
        if (target % 2 == 0)
        {
            target++;
        }

        var windowSize = Math.Min(target, recordCount);
        if (windowSize % 2 == 0)
        {
            windowSize--;
        }

        if (windowSize < 5)
        {
            windowSize = 5;
        }

        return SavitzkyGolay.Create(windowSize, 1, 3);
    }

    private static double CalculateAnomalyRate(int anomalyCount, int sampleCount, int sampleRate)
    {
        return sampleCount == 0
            ? 0
            : (double)anomalyCount / sampleCount * sampleRate;
    }

    private static TemperatureAverage[] CalculateTemperatureAverages(IReadOnlyCollection<TemperatureSample> samples)
    {
        return samples
            .GroupBy(sample => sample.LocationId)
            .OrderBy(group => group.Key)
            .Select(group => new TemperatureAverage(group.Key, group.Average(sample => sample.TemperatureCelsius)))
            .ToArray();
    }

    #endregion

    #region PSST conversion

    public static TelemetryData FromRecording(RawTelemetryData rawData, Metadata metadata, BikeData bikeData)
    {
        return FromRecording(rawData, metadata, bikeData, TelemetryProcessingOptions.Default);
    }

    public static TelemetryData FromRecording(
        RawTelemetryData rawData,
        Metadata metadata,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions)
    {
        return FromRecording(rawData, metadata, bikeData, processingOptions, logLifecycle: true);
    }

    private static TelemetryData FromRecording(
        RawTelemetryData rawData,
        Metadata metadata,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions,
        bool logLifecycle)
    {
        ArgumentNullException.ThrowIfNull(processingOptions);

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
        td.TemperatureAverages = CalculateTemperatureAverages(rawData.TemperatureData);

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

        if (recordCount < 5)
        {
            td.Front.Present = false;
            td.Rear.Present = false;
            td.CalculateAirTimes();
            return td;
        }

        // Create a velocity filter that matches the capture size. Live captures may be
        // shorter than a full SST import during early-session save or stats recompute.
        var filter = CreateVelocityFilter(recordCount, td.Metadata.SampleRate, processingOptions);

        ProcessSuspensionSide(
            td.Front,
            rawData.Front,
            bikeData.FrontMeasurementWraps,
            bikeData.FrontMeasurementToTravel,
            td.Metadata.SampleRate,
            time,
            filter);
        ProcessSuspensionSide(
            td.Rear,
            rawData.Rear,
            bikeData.RearMeasurementWraps,
            bikeData.RearMeasurementToTravel,
            td.Metadata.SampleRate,
            time,
            filter);

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
        return FromLiveCapture(capture, TelemetryProcessingOptions.Default);
    }

    public static TelemetryData FromLiveCapture(
        LiveTelemetryCapture capture,
        TelemetryProcessingOptions processingOptions)
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

        return FromRecording(rawData, capture.Metadata, capture.BikeData, processingOptions, logLifecycle: false);
    }

    #endregion
}
