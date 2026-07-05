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
    public RawStreamGap[] StreamGaps { get; set; } = [];
    public SstFinalStatus? FinalStatus { get; set; }
    public bool MissingFinalStatus { get; set; }
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
        var telemetryData = MessagePackSerializer.Deserialize<TelemetryData>(data);
        NormalizeSegmentSampleCounts(telemetryData.Front);
        NormalizeSegmentSampleCounts(telemetryData.Rear);
        return telemetryData;
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
                        Start = StrokeStartSeconds(f, r),
                        End = StrokeEndSeconds(f, r)
                    };
                    airtimes.Add(at);
                    break;
                }
            }

            var maxMean = (Front.MaxTravel + Rear.MaxTravel) / 2.0;

            foreach (var f in Front.Strokes.Idlings)
            {
                if (!f.AirCandidate) continue;
                if (!TryGetMeanTravel(Front, f, out var fMean) ||
                    !TryGetMeanTravel(Rear, f, out var rMean))
                {
                    continue;
                }

                if (!((fMean + rMean) / 2 <= maxMean * Parameters.AirtimeTravelMeanThresholdRatio)) continue;
                var at = new Airtime
                {
                    Start = StrokeStartSeconds(f),
                    End = StrokeEndSeconds(f)
                };
                airtimes.Add(at);
            }

            foreach (var r in Rear.Strokes.Idlings)
            {
                if (!r.AirCandidate) continue;
                if (!TryGetMeanTravel(Front, r, out var fMean) ||
                    !TryGetMeanTravel(Rear, r, out var rMean))
                {
                    continue;
                }

                if (!((fMean + rMean) / 2 <= maxMean * Parameters.AirtimeTravelMeanThresholdRatio)) continue;
                var at = new Airtime
                {
                    Start = StrokeStartSeconds(r),
                    End = StrokeEndSeconds(r)
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
                    Start = StrokeStartSeconds(f),
                    End = StrokeEndSeconds(f)
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
                    Start = StrokeStartSeconds(r),
                    End = StrokeEndSeconds(r)
                };
                airtimes.Add(at);
            }
        }

        Airtimes = [.. airtimes];
    }

    private double StrokeStartSeconds(Stroke first, Stroke? second = null)
    {
        if (second is not null &&
            first.EndSeconds > first.StartSeconds &&
            second.EndSeconds > second.StartSeconds)
        {
            return Math.Max(first.StartSeconds, second.StartSeconds);
        }

        return StrokeStartSeconds(first);
    }

    private double StrokeEndSeconds(Stroke first, Stroke? second = null)
    {
        if (second is not null &&
            first.EndSeconds > first.StartSeconds &&
            second.EndSeconds > second.StartSeconds)
        {
            return Math.Min(first.EndSeconds, second.EndSeconds);
        }

        return StrokeEndSeconds(first);
    }

    private double StrokeStartSeconds(Stroke stroke) =>
        stroke.EndSeconds > stroke.StartSeconds ? stroke.StartSeconds : stroke.Start / (double)Metadata.SampleRate;

    private double StrokeEndSeconds(Stroke stroke) =>
        stroke.EndSeconds > stroke.StartSeconds ? stroke.EndSeconds : stroke.End / (double)Metadata.SampleRate;

    private bool TryGetMeanTravel(Suspension suspension, Stroke stroke, out double mean)
    {
        if (!suspension.HasGaps)
        {
            if (suspension.Travel.Length == 0)
            {
                mean = 0;
                return false;
            }

            var start = Math.Clamp(stroke.Start, 0, suspension.Travel.Length - 1);
            var end = Math.Clamp(stroke.End, 0, suspension.Travel.Length - 1);
            if (end < start)
            {
                mean = 0;
                return false;
            }

            mean = suspension.Travel[start..(end + 1)].Mean();
            return true;
        }

        var sampler = new SuspensionTimeSeriesSampler(suspension.Segments, suspension.Travel, Metadata.SampleRate);
        var startSeconds = StrokeStartSeconds(stroke);
        var endSeconds = StrokeEndSeconds(stroke);
        var values = new List<double>();
        for (var seconds = startSeconds; seconds <= endSeconds; seconds += 1.0 / Metadata.SampleRate)
        {
            if (sampler.TrySampleTravel(seconds, out var travel))
            {
                values.Add(travel);
            }
        }

        mean = values.Count == 0 ? 0 : values.Average();
        return values.Count > 0;
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
        if (trace.Travel.Length == 0)
        {
            suspension.Segments = [];
        }
        else
        {
            suspension.Segments =
            [
                new ProcessedSuspensionSegment
                {
                    FirstDenseIndex = 0,
                    FirstSourceIndex = 0,
                    StartSeconds = 0,
                    SampleCount = trace.Travel.Length,
                },
            ];
        }

        suspension.HasGaps = false;
    }

    private static bool UsesSegmentAwareTravel(RawTelemetryData rawData) =>
        rawData.Version == SstV5Constants.Version &&
        (rawData.FrontSegments.Length > 1 ||
         rawData.RearSegments.Length > 1 ||
         rawData.StreamGaps.Any(gap => gap.StreamKind == SstV5Constants.StreamTravel));

    // A travel gap tagged with a side's sensor bit belongs only to that side. A travel gap with
    // no location (e.g. a stream-level final-status counter) is not side-attributable, so it is
    // treated as affecting both sides.
    private static bool HasTravelGapForSide(RawStreamGap[] streamGaps, uint sensorBit) =>
        streamGaps.Any(gap =>
            gap.StreamKind == SstV5Constants.StreamTravel &&
            (gap.LocationId is null || gap.LocationId == (byte)sensorBit));

    private static void ProcessSuspensionSide(
        Suspension suspension,
        ushort[] rawSamples,
        bool measurementWraps,
        Func<ushort, double>? measurementToTravel,
        int sampleRate,
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
            filter);

        ApplySuspensionTrace(suspension, trace);
        suspension.AnomalyRate = CalculateAnomalyRate(preprocessed.AnomalyCount, preprocessed.Samples.Length, sampleRate);
    }

    private static void ProcessSegmentAwareSuspensionSide(
        Suspension suspension,
        RawCountSegment[] rawSegments,
        bool measurementWraps,
        Func<ushort, double>? measurementToTravel,
        int sampleRate,
        TelemetryProcessingOptions processingOptions)
    {
        if (rawSegments.Length == 0)
        {
            suspension.Present = false;
            suspension.Travel = [];
            suspension.Velocity = [];
            suspension.Strokes = new Strokes();
            suspension.TravelBins = [];
            suspension.VelocityBins = [];
            suspension.FineVelocityBins = [];
            suspension.Segments = [];
            suspension.HasGaps = false;
            return;
        }

        Debug.Assert(measurementToTravel is not null);
        if (measurementToTravel is null)
        {
            throw new InvalidOperationException("Present suspension is missing travel calibration.");
        }

        var travelValues = new List<double>();
        var velocityValues = new List<double>();
        var processedSegments = new List<ProcessedSuspensionSegment>();
        var compressions = new List<Stroke>();
        var rebounds = new List<Stroke>();
        var idlings = new List<Stroke>();
        var anomalyCount = 0;
        var sampleCount = 0;

        foreach (var rawSegment in rawSegments.OrderBy(segment => segment.FirstIndex))
        {
            if (rawSegment.Counts.Length == 0)
            {
                continue;
            }

            var denseOffset = travelValues.Count;
            var segmentStartSeconds = rawSegment.FirstMonotonicDeltaUs / 1_000_000.0;
            var preprocessed = MeasurementPreprocessor.Process(
                rawSegment.Counts,
                MeasurementPreprocessor.SensorTypeForWrapping(measurementWraps),
                sampleRate);
            anomalyCount += preprocessed.AnomalyCount;
            sampleCount += preprocessed.Samples.Length;

            double[] segmentTravel;
            double[] segmentVelocity;
            Strokes segmentStrokes;
            if (preprocessed.Samples.Length < 5)
            {
                segmentTravel = CalculateTravel(preprocessed.Samples, suspension.MaxTravel!.Value, measurementToTravel);
                segmentVelocity = new double[segmentTravel.Length];
                segmentStrokes = Strokes.FromCategorized([], [], []);
            }
            else
            {
                var segmentFilter = CreateVelocityFilter(preprocessed.Samples.Length, sampleRate, processingOptions);
                var trace = SuspensionTraceProcessor.Process(
                    preprocessed.Samples,
                    suspension.MaxTravel!.Value,
                    measurementToTravel,
                    sampleRate,
                    segmentFilter);
                segmentTravel = trace.Travel;
                segmentVelocity = trace.Velocity;
                segmentStrokes = trace.Strokes;
                OffsetStrokeTimes(segmentStrokes, denseOffset, segmentStartSeconds, sampleRate);
            }

            travelValues.AddRange(segmentTravel);
            velocityValues.AddRange(segmentVelocity);
            processedSegments.Add(new ProcessedSuspensionSegment
            {
                FirstDenseIndex = denseOffset,
                FirstSourceIndex = rawSegment.FirstIndex,
                StartSeconds = segmentStartSeconds,
                SampleCount = segmentTravel.Length,
            });
            compressions.AddRange(segmentStrokes.Compressions);
            rebounds.AddRange(segmentStrokes.Rebounds);
            idlings.AddRange(segmentStrokes.Idlings);
        }

        suspension.Present = travelValues.Count > 0;
        suspension.Travel = travelValues.ToArray();
        suspension.Velocity = velocityValues.ToArray();
        suspension.Strokes = Strokes.FromCategorized([.. compressions], [.. rebounds], [.. idlings]);
        suspension.TravelBins = suspension.MaxTravel is > 0
            ? HistogramBuilder.Linspace(0, suspension.MaxTravel.Value, Parameters.TravelHistBins + 1)
            : [];
        var velocityForBins = suspension.Velocity.Length == 0 ? [0.0] : suspension.Velocity;
        suspension.VelocityBins = HistogramBuilder.DigitizeVelocity(velocityForBins, Parameters.VelocityHistStep).Bins;
        suspension.FineVelocityBins = HistogramBuilder.DigitizeVelocity(velocityForBins, Parameters.VelocityHistStepFine).Bins;
        suspension.Segments = [.. processedSegments];
        suspension.HasGaps = processedSegments.Count > 1;
        suspension.AnomalyRate = CalculateAnomalyRate(anomalyCount, sampleCount, sampleRate);
    }

    private static double[] CalculateTravel(
        ushort[] measurements,
        double maxTravel,
        Func<ushort, double> measurementToTravel)
    {
        var travel = new double[measurements.Length];
        for (var index = 0; index < measurements.Length; index++)
        {
            travel[index] = Math.Clamp(measurementToTravel(measurements[index]), 0, maxTravel);
        }

        return travel;
    }

    private static void OffsetStrokeTimes(Strokes strokes, int denseOffset, double segmentStartSeconds, int sampleRate)
    {
        foreach (var stroke in strokes.Compressions.Concat(strokes.Rebounds).Concat(strokes.Idlings))
        {
            var localStart = stroke.Start;
            var localEnd = stroke.End;
            stroke.Start += denseOffset;
            stroke.End += denseOffset;
            stroke.StartSeconds = segmentStartSeconds + localStart / (double)sampleRate;
            stroke.EndSeconds = segmentStartSeconds + localEnd / (double)sampleRate;
        }
    }

    private static void NormalizeSegmentSampleCounts(Suspension? suspension)
    {
        if (suspension is null)
        {
            return;
        }

        if (suspension.Segments is not { Length: > 0 } segments)
        {
            suspension.Segments = [];
            return;
        }

        if (segments.All(segment => segment.SampleCount > 0))
        {
            return;
        }

        var travelLength = suspension.Travel?.Length ?? 0;
        if (travelLength == 0)
        {
            foreach (var segment in segments)
            {
                segment.SampleCount = 0;
            }

            return;
        }

        for (var index = 0; index < segments.Length - 1; index++)
        {
            segments[index].SampleCount =
                segments[index + 1].FirstDenseIndex - segments[index].FirstDenseIndex;
        }

        var last = segments[^1];
        last.SampleCount = travelLength - last.FirstDenseIndex;
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

        var target = processingOptions.VelocityFilterWindowSamples(sampleRate);

        var windowSize = Math.Min(target, recordCount);
        if (windowSize % 2 == 0)
        {
            windowSize--;
        }

        if (windowSize < TelemetryProcessingOptions.MinVelocityFilterWindowSamples)
        {
            windowSize = TelemetryProcessingOptions.MinVelocityFilterWindowSamples;
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
        td.StreamGaps = rawData.StreamGaps;
        td.FinalStatus = rawData.FinalStatus;
        td.MissingFinalStatus = rawData.MissingFinalStatus;

        if (UsesSegmentAwareTravel(rawData))
        {
            td.Front.Present = rawData.FrontSegments.Length > 0;
            td.Rear.Present = rawData.RearSegments.Length > 0;
            if (!td.Front.Present && !td.Rear.Present)
            {
                throw new Exception("Front and rear record arrays are empty!");
            }

            Parallel.Invoke(
                () => ProcessSegmentAwareSuspensionSide(
                    td.Front,
                    rawData.FrontSegments,
                    bikeData.FrontMeasurementWraps,
                    bikeData.FrontMeasurementToTravel,
                    td.Metadata.SampleRate,
                    processingOptions),
                () => ProcessSegmentAwareSuspensionSide(
                    td.Rear,
                    rawData.RearSegments,
                    bikeData.RearMeasurementWraps,
                    bikeData.RearMeasurementToTravel,
                    td.Metadata.SampleRate,
                    processingOptions));

            td.Front.HasGaps = td.Front.HasGaps || HasTravelGapForSide(rawData.StreamGaps, SstV5Constants.SensorForkTravel);
            td.Rear.HasGaps = td.Rear.HasGaps || HasTravelGapForSide(rawData.StreamGaps, SstV5Constants.SensorShockTravel);
            td.CalculateAirTimes();

            if (logLifecycle)
            {
                logger.Verbose(
                    "Segment-aware telemetry processing completed with front present {FrontPresent}, rear present {RearPresent}, {AirtimeCount} airtimes, {MarkerCount} markers, IMU present {HasImuData}, and GPS points {GpsPointCount}",
                    td.Front.Present,
                    td.Rear.Present,
                    td.Airtimes.Length,
                    td.Markers.Length,
                    td.ImuData is not null,
                    td.GpsData?.Length ?? 0);
            }

            return td;
        }

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

        var recordCount = Math.Max(fc, rc);

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

        Parallel.Invoke(
            () => ProcessSuspensionSide(
                td.Front,
                rawData.Front,
                bikeData.FrontMeasurementWraps,
                bikeData.FrontMeasurementToTravel,
                td.Metadata.SampleRate,
                filter),
            () => ProcessSuspensionSide(
                td.Rear,
                rawData.Rear,
                bikeData.RearMeasurementWraps,
                bikeData.RearMeasurementToTravel,
                td.Metadata.SampleRate,
                filter));

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
        var version = UsesSegmentAwareLiveCapture(capture)
            ? SstV5Constants.Version
            : capture.Metadata.Version;
        var metadata = new Metadata
        {
            SourceName = capture.Metadata.SourceName,
            Version = version,
            SampleRate = capture.Metadata.SampleRate,
            Timestamp = capture.Metadata.Timestamp,
            Duration = capture.Metadata.Duration,
        };
        var front = capture.FrontMeasurements;
        var rear = capture.RearMeasurements;
        var rawData = new RawTelemetryData
        {
            Version = (byte)Math.Clamp(version, byte.MinValue, byte.MaxValue),
            SampleRate = (ushort)Math.Clamp(capture.Metadata.SampleRate, 0, ushort.MaxValue),
            Timestamp = capture.Metadata.Timestamp,
            Front = front,
            Rear = rear,
            FrontSegments = capture.FrontSegments.ToArray(),
            RearSegments = capture.RearSegments.ToArray(),
            StreamGaps = capture.StreamGaps.ToArray(),
            FinalStatus = capture.FinalStatus,
            MissingFinalStatus = capture.MissingFinalStatus,
            SessionStartUtcMs = checked(capture.Metadata.Timestamp * 1000),
            RecordingDurationSeconds = capture.Metadata.Duration,
            Markers = capture.Markers,
            ImuData = capture.ImuData,
            GpsData = capture.GpsData,
        };

        if (rawData.FrontSegments.Length == 0 && rawData.Front.Length > 0)
        {
            rawData.FrontSegments =
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = rawData.Front,
                }
            ];
        }

        if (rawData.RearSegments.Length == 0 && rawData.Rear.Length > 0)
        {
            rawData.RearSegments =
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = rawData.Rear,
                }
            ];
        }

        return FromRecording(rawData, metadata, capture.BikeData, processingOptions, logLifecycle: false);
    }

    private static bool UsesSegmentAwareLiveCapture(LiveTelemetryCapture capture) =>
        capture.StreamGaps.Length > 0 ||
        capture.FinalStatus is not null ||
        capture.MissingFinalStatus ||
        HasNonDenseSegments(capture.FrontSegments) ||
        HasNonDenseSegments(capture.RearSegments) ||
        capture.ImuData?.HasGaps == true;

    private static bool HasNonDenseSegments(RawCountSegment[] segments) =>
        segments.Length > 1 ||
        segments.Any(segment => segment.FirstIndex != 0 || segment.FirstMonotonicDeltaUs != 0);

    #endregion
}
