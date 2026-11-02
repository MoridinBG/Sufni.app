using System.Diagnostics;
using MathNet.Numerics.Statistics;
using MessagePack;

#pragma warning disable CS8618

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TelemetryData
{
    private readonly Lazy<StrokeCoarseIndexes> strokeCoarseIndexes;

    public const int TravelBinsForVelocityHistogram = 10;

    #region Public properties

    public Metadata Metadata { get; init; }
    public Suspension Front { get; init; }
    public Suspension Rear { get; init; }
    public Airtime[] Airtimes { get; init; }
    public MarkerData[] Markers { get; init; } = [];
    public RawImuData? ImuData { get; init; }
    public GpsRecord[]? GpsData { get; init; }
    public TemperatureAverage[] TemperatureAverages { get; init; } = [];
    [IgnoreMember] public byte[] BinaryForm => TelemetryCodec.Encode(this);
    [IgnoreMember] internal StrokeCoarseIndexes CoarseStrokeIndexes => strokeCoarseIndexes.Value;

    #endregion

    #region Constructors / Initializers

    public TelemetryData()
    {
        strokeCoarseIndexes = new Lazy<StrokeCoarseIndexes>(
            () => new StrokeCoarseIndexes(this),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private TelemetryData(TelemetryData source)
    {
        strokeCoarseIndexes = new Lazy<StrokeCoarseIndexes>(
            () => new StrokeCoarseIndexes(this), LazyThreadSafetyMode.ExecutionAndPublication);
        Metadata = source.Metadata;
        Front = source.Front;
        Rear = source.Rear;
        Airtimes = source.Airtimes;
        Markers = source.Markers;
        ImuData = source.ImuData;
        GpsData = source.GpsData;
        TemperatureAverages = source.TemperatureAverages;
    }

    public static TelemetryData FromBinary(byte[]? data) => TelemetryCodec.Decode(data);

    #endregion

    #region Private helpers for ProcessRecording

    private static Airtime[] CalculateAirTimes(Suspension front, Suspension rear, int sampleRate,
        StrokeCandidate[] frontIdlings, StrokeCandidate[] rearIdlings)
    {
        var airtimes = new List<Airtime>();

        if (front.Present && rear.Present)
        {
            foreach (var f in frontIdlings)
            {
                if (!f.AirCandidate) continue;
                foreach (var r in rearIdlings)
                {
                    if (!r.AirCandidate || !f.Overlaps(r)) continue;
                    f.AirCandidate = false;
                    r.AirCandidate = false;

                    var at = new Airtime
                    {
                        Start = StrokeStartSeconds(f, sampleRate, r),
                        End = StrokeEndSeconds(f, sampleRate, r)
                    };
                    airtimes.Add(at);
                    break;
                }
            }

            var maxMean = (front.MaxTravel + rear.MaxTravel) / 2.0;

            foreach (var f in frontIdlings)
            {
                if (!f.AirCandidate) continue;
                if (!TryGetMeanTravel(front, f, sampleRate, out var fMean) ||
                    !TryGetMeanTravel(rear, f, sampleRate, out var rMean))
                {
                    continue;
                }

                if (!((fMean + rMean) / 2 <= maxMean * Parameters.AirtimeTravelMeanThresholdRatio)) continue;
                var at = new Airtime
                {
                    Start = StrokeStartSeconds(f, sampleRate),
                    End = StrokeEndSeconds(f, sampleRate)
                };
                airtimes.Add(at);
            }

            foreach (var r in rearIdlings)
            {
                if (!r.AirCandidate) continue;
                if (!TryGetMeanTravel(front, r, sampleRate, out var fMean) ||
                    !TryGetMeanTravel(rear, r, sampleRate, out var rMean))
                {
                    continue;
                }

                if (!((fMean + rMean) / 2 <= maxMean * Parameters.AirtimeTravelMeanThresholdRatio)) continue;
                var at = new Airtime
                {
                    Start = StrokeStartSeconds(r, sampleRate),
                    End = StrokeEndSeconds(r, sampleRate)
                };
                airtimes.Add(at);
            }
        }
        else if (front.Present)
        {
            foreach (var f in frontIdlings)
            {
                if (!f.AirCandidate) continue;
                var at = new Airtime
                {
                    Start = StrokeStartSeconds(f, sampleRate),
                    End = StrokeEndSeconds(f, sampleRate)
                };
                airtimes.Add(at);
            }
        }
        else if (rear.Present)
        {
            foreach (var r in rearIdlings)
            {
                if (!r.AirCandidate) continue;
                var at = new Airtime
                {
                    Start = StrokeStartSeconds(r, sampleRate),
                    End = StrokeEndSeconds(r, sampleRate)
                };
                airtimes.Add(at);
            }
        }

        return [.. airtimes];
    }

    private static double StrokeStartSeconds(Stroke first, int sampleRate, Stroke? second = null)
    {
        if (second is not null &&
            first.EndSeconds > first.StartSeconds &&
            second.EndSeconds > second.StartSeconds)
        {
            return Math.Max(first.StartSeconds, second.StartSeconds);
        }

        return StrokeStartSeconds(first, sampleRate);
    }

    private static double StrokeEndSeconds(Stroke first, int sampleRate, Stroke? second = null)
    {
        if (second is not null &&
            first.EndSeconds > first.StartSeconds &&
            second.EndSeconds > second.StartSeconds)
        {
            return Math.Min(first.EndSeconds, second.EndSeconds);
        }

        return StrokeEndSeconds(first, sampleRate);
    }

    private static double StrokeStartSeconds(Stroke stroke, int sampleRate) =>
        stroke.EndSeconds > stroke.StartSeconds ? stroke.StartSeconds : stroke.Start / (double)sampleRate;

    private static double StrokeEndSeconds(Stroke stroke, int sampleRate) =>
        stroke.EndSeconds > stroke.StartSeconds ? stroke.EndSeconds : stroke.End / (double)sampleRate;

    private static bool TryGetMeanTravel(Suspension suspension, Stroke stroke, int sampleRate, out double mean)
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

        var sampler = new SuspensionTimeSeriesSampler(suspension.Segments, suspension.Travel, sampleRate);
        var startSeconds = StrokeStartSeconds(stroke, sampleRate);
        var endSeconds = StrokeEndSeconds(stroke, sampleRate);
        var values = new List<double>();
        for (var seconds = startSeconds; seconds <= endSeconds; seconds += 1.0 / sampleRate)
        {
            if (sampler.TrySampleTravel(seconds, out var travel))
            {
                values.Add(travel);
            }
        }

        mean = values.Count == 0 ? 0 : values.Average();
        return values.Count > 0;
    }

    private static bool UsesSegmentAwareTravel(RawTelemetryData rawData) =>
        rawData.Version == SstV5ProtocolConstants.Version &&
        (rawData.FrontSegments.Length > 1 ||
         rawData.RearSegments.Length > 1 ||
         rawData.StreamGaps.Any(gap => gap.StreamKind == SstV5ProtocolConstants.StreamTravel));

    // A travel gap tagged with a side's sensor bit belongs only to that side. A travel gap with
    // no location (e.g. a stream-level final-status counter) is not side-attributable, so it is
    // treated as affecting both sides.
    private static bool HasTravelGapForSide(RawStreamGap[] streamGaps, uint sensorBit) =>
        streamGaps.Any(gap =>
            gap.StreamKind == SstV5ProtocolConstants.StreamTravel &&
            (gap.LocationId is null || gap.LocationId == (byte)sensorBit));

    private static ProcessedSide ProcessSuspensionSide(
        double? maxTravel, bool present, ushort[] rawSamples, bool measurementWraps,
        Func<ushort, double>? measurementToTravel, int sampleRate, SavitzkyGolay? filter)
    {
        if (!present) return new(new Suspension { MaxTravel = maxTravel, Strokes = new Strokes() }, []);
        Debug.Assert(measurementToTravel is not null);
        var preprocessed = MeasurementPreprocessor.Process(rawSamples,
            MeasurementPreprocessor.SensorTypeForWrapping(measurementWraps), sampleRate);
        var trace = SuspensionTraceProcessor.Process(preprocessed.Samples, maxTravel!.Value, measurementToTravel, sampleRate, filter);
        return new(new Suspension
        {
            Present = true,
            MaxTravel = maxTravel,
            Travel = trace.Travel,
            Velocity = trace.Velocity,
            Strokes = trace.Strokes,
            TravelBins = trace.TravelBins,
            VelocityBins = trace.VelocityBins,
            Segments = trace.Travel.Length == 0 ? [] : [new ProcessedSuspensionSegment { SampleCount = trace.Travel.Length }],
            AnomalyRate = CalculateAnomalyRate(preprocessed.AnomalyCount, preprocessed.Samples.Length, sampleRate),
        }, trace.Idlings);
    }

    private static ProcessedSide ProcessSegmentAwareSuspensionSide(
        double? maxTravel, bool present, bool hasGap,
        RawCountSegment[] rawSegments,
        bool measurementWraps,
        Func<ushort, double>? measurementToTravel,
        int sampleRate,
        TelemetryProcessingOptions processingOptions)
    {
        if (!present) return new(new Suspension
        {
            MaxTravel = maxTravel, Travel = [], Velocity = [], Strokes = new Strokes(), TravelBins = [], VelocityBins = [],
        }, []);

        Debug.Assert(measurementToTravel is not null);

        var travelValues = new List<double>();
        var velocityValues = new List<double>();
        var processedSegments = new List<ProcessedSuspensionSegment>();
        var compressions = new List<Stroke>();
        var rebounds = new List<Stroke>();
        var idlings = new List<StrokeCandidate>();
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
            StrokeCandidate[] segmentIdlings = [];
            if (preprocessed.Samples.Length < 5)
            {
                segmentTravel = CalculateTravel(preprocessed.Samples, maxTravel!.Value, measurementToTravel);
                segmentVelocity = new double[segmentTravel.Length];
                segmentStrokes = new Strokes { Compressions = [], Rebounds = [] };
            }
            else
            {
                var segmentFilter = CreateVelocityFilter(preprocessed.Samples.Length, sampleRate, processingOptions);
                var trace = SuspensionTraceProcessor.Process(
                    preprocessed.Samples,
                    maxTravel!.Value,
                    measurementToTravel,
                    sampleRate,
                    segmentFilter);
                segmentTravel = trace.Travel;
                segmentVelocity = trace.Velocity;
                segmentStrokes = trace.Strokes;
                segmentIdlings = trace.Idlings;
                segmentStrokes = OffsetStrokeTimes(segmentStrokes, segmentIdlings, denseOffset, segmentStartSeconds, sampleRate);
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
            idlings.AddRange(segmentIdlings);
        }

        var travel = travelValues.ToArray();
        var velocity = velocityValues.ToArray();
        return new(new Suspension
        {
            Present = travel.Length > 0,
            MaxTravel = maxTravel,
            Travel = travel,
            Velocity = velocity,
            Strokes = new Strokes { Compressions = [.. compressions], Rebounds = [.. rebounds] },
            TravelBins = maxTravel is > 0 ? HistogramBuilder.Linspace(0, maxTravel.Value, Parameters.TravelHistBins + 1) : [],
            VelocityBins = HistogramBuilder.CreateVelocityBins(velocity.Length == 0 ? [0.0] : velocity, Parameters.VelocityHistStep),
            Segments = [.. processedSegments],
            HasGaps = travel.Length > 0 && (processedSegments.Count > 1 || hasGap),
            AnomalyRate = CalculateAnomalyRate(anomalyCount, sampleCount, sampleRate),
        }, [.. idlings]);
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

    private static Strokes OffsetStrokeTimes(Strokes strokes, StrokeCandidate[] idlings, int denseOffset, double segmentStartSeconds, int sampleRate)
    {
        Stroke Offset(Stroke stroke) => stroke with
        {
            Start = stroke.Start + denseOffset,
            End = stroke.End + denseOffset,
            StartSeconds = segmentStartSeconds + stroke.Start / (double)sampleRate,
            EndSeconds = segmentStartSeconds + stroke.End / (double)sampleRate,
        };
        for (var i = 0; i < idlings.Length; i++) idlings[i] = (StrokeCandidate)Offset(idlings[i]);
        return new Strokes { Compressions = strokes.Compressions.Select(Offset).ToArray(), Rebounds = strokes.Rebounds.Select(Offset).ToArray() };
    }

    private sealed record ProcessedSide(Suspension Suspension, StrokeCandidate[] Idlings);

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

    private static bool HasUsableTravel(IEnumerable<ushort> samples, double? maxTravel, Func<ushort, double>? calibration)
    {
        if (maxTravel is null || calibration is null) return false;
        var hasNonZero = false;
        var hasNonMax = false;
        foreach (var sample in samples)
        {
            hasNonZero |= sample != 0;
            hasNonMax |= sample != ushort.MaxValue;
            if (hasNonZero && hasNonMax) return true;
        }
        return false;
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
        ArgumentNullException.ThrowIfNull(processingOptions);

        ProcessedSide? front = null, rear = null;
        if (UsesSegmentAwareTravel(rawData))
        {
            var frontPresent = HasUsableTravel(rawData.FrontSegments.SelectMany(segment => segment.Counts), bikeData.FrontMaxTravel, bikeData.FrontMeasurementToTravel);
            var rearPresent = HasUsableTravel(rawData.RearSegments.SelectMany(segment => segment.Counts), bikeData.RearMaxTravel, bikeData.RearMeasurementToTravel);
            if (!frontPresent && !rearPresent) throw new TelemetryProcessingException(TelemetryProcessingFailureReason.NoTravelData);
            Parallel.Invoke(
                () => front = ProcessSegmentAwareSuspensionSide(bikeData.FrontMaxTravel, frontPresent,
                    HasTravelGapForSide(rawData.StreamGaps, SstV5ProtocolConstants.SensorForkTravel), rawData.FrontSegments,
                    bikeData.FrontMeasurementWraps, bikeData.FrontMeasurementToTravel, metadata.SampleRate, processingOptions),
                () => rear = ProcessSegmentAwareSuspensionSide(bikeData.RearMaxTravel, rearPresent,
                    HasTravelGapForSide(rawData.StreamGaps, SstV5ProtocolConstants.SensorShockTravel), rawData.RearSegments,
                    bikeData.RearMeasurementWraps, bikeData.RearMeasurementToTravel, metadata.SampleRate, processingOptions));
        }
        else
        {
            var frontPresent = HasUsableTravel(rawData.Front, bikeData.FrontMaxTravel, bikeData.FrontMeasurementToTravel);
            var rearPresent = HasUsableTravel(rawData.Rear, bikeData.RearMaxTravel, bikeData.RearMeasurementToTravel);
            if (!frontPresent && !rearPresent) throw new TelemetryProcessingException(TelemetryProcessingFailureReason.NoTravelData);
            if (frontPresent && rearPresent && rawData.Front.Length != rawData.Rear.Length)
                throw new TelemetryProcessingException(TelemetryProcessingFailureReason.RecordCountMismatch);
            var count = frontPresent ? rawData.Front.Length : rawData.Rear.Length;
            var filter = count < 5 ? null : CreateVelocityFilter(count, metadata.SampleRate, processingOptions);
            Parallel.Invoke(
                () => front = ProcessSuspensionSide(bikeData.FrontMaxTravel, frontPresent && count >= 5, rawData.Front,
                    bikeData.FrontMeasurementWraps, bikeData.FrontMeasurementToTravel, metadata.SampleRate, filter),
                () => rear = ProcessSuspensionSide(bikeData.RearMaxTravel, rearPresent && count >= 5, rawData.Rear,
                    bikeData.RearMeasurementWraps, bikeData.RearMeasurementToTravel, metadata.SampleRate, filter));
        }
        return new TelemetryData
        {
            Metadata = metadata,
            Front = front!.Suspension,
            Rear = rear!.Suspension,
            Airtimes = CalculateAirTimes(front.Suspension, rear.Suspension, metadata.SampleRate, front.Idlings, rear.Idlings),
            Markers = rawData.Markers,
            ImuData = rawData.ImuData,
            GpsData = rawData.GpsData,
            TemperatureAverages = CalculateTemperatureAverages(rawData.TemperatureData),
        };
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
            ? SstV5ProtocolConstants.Version
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
            TemperatureData = capture.TemperatureData,
        };

        return FromRecording(rawData, metadata, capture.BikeData, processingOptions);
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
