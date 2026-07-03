namespace Sufni.Telemetry;

internal static class TelemetrySlicer
{
    public static RawTelemetryData Slice(RawTelemetryData raw, double startSeconds, double? endSeconds)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var window = CreateWindow(startSeconds, endSeconds, GetRawDurationSeconds(raw), raw.SampleRate);
        var streamGaps = SliceStreamGaps(raw.StreamGaps, window, raw.ImuData?.SampleRate);
        var frontSegments = SliceCountSegments(raw.FrontSegments, window, raw.SampleRate);
        var rearSegments = SliceCountSegments(raw.RearSegments, window, raw.SampleRate);

        return new RawTelemetryData
        {
            Magic = raw.Magic?.ToArray() ?? [],
            Version = raw.Version,
            SampleRate = raw.SampleRate,
            Timestamp = checked(raw.Timestamp + (long)Math.Floor(startSeconds)),
            Front = frontSegments.Length > 0 ? FlattenCounts(frontSegments) : SliceDense(raw.Front, window),
            Rear = rearSegments.Length > 0 ? FlattenCounts(rearSegments) : SliceDense(raw.Rear, window),
            FrontAnomalyRate = raw.FrontAnomalyRate,
            RearAnomalyRate = raw.RearAnomalyRate,
            Markers = SliceMarkers(raw.Markers, window),
            ImuData = SliceImuData(raw.ImuData, window, streamGaps),
            GpsData = SliceGpsData(raw.GpsData, RawBaseUtcMs(raw), window),
            TemperatureData = SliceTemperatureData(raw.TemperatureData, RawBaseUtcSeconds(raw), window),
            Malformed = raw.Malformed,
            MalformedMessage = raw.MalformedMessage,
            SessionStartUtcMs = raw.SessionStartUtcMs == 0
                ? 0
                : checked(raw.SessionStartUtcMs + RoundMilliseconds(startSeconds)),
            RecordingDurationSeconds = window.DurationSeconds,
            FrontSegments = frontSegments,
            RearSegments = rearSegments,
            StreamGaps = streamGaps,
            FinalStatus = SliceFinalStatus(raw.FinalStatus, window, GetRawDurationSeconds(raw)),
            MissingFinalStatus = window.ReachesSourceEnd ? raw.MissingFinalStatus : false,
        };
    }

    public static LiveTelemetryCapture Slice(LiveTelemetryCapture capture, double startSeconds, double? endSeconds)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var sampleRate = checked((ushort)Math.Clamp(capture.Metadata.SampleRate, 0, ushort.MaxValue));
        var window = CreateWindow(startSeconds, endSeconds, capture.Metadata.Duration, sampleRate);
        var streamGaps = SliceStreamGaps(capture.StreamGaps, window, capture.ImuData?.SampleRate);
        var frontSegments = SliceCountSegments(capture.FrontSegments, window, sampleRate);
        var rearSegments = SliceCountSegments(capture.RearSegments, window, sampleRate);
        var metadata = new Metadata
        {
            SourceName = capture.Metadata.SourceName,
            Version = capture.Metadata.Version,
            SampleRate = capture.Metadata.SampleRate,
            Timestamp = checked(capture.Metadata.Timestamp + (long)Math.Floor(startSeconds)),
            Duration = window.DurationSeconds,
        };

        return new LiveTelemetryCapture(
            metadata,
            capture.BikeData,
            frontSegments,
            rearSegments,
            SliceImuData(capture.ImuData, window, streamGaps),
            SliceGpsData(capture.GpsData, checked(capture.Metadata.Timestamp * 1000), window),
            SliceMarkers(capture.Markers, window),
            streamGaps,
            SliceFinalStatus(capture.FinalStatus, window, capture.Metadata.Duration),
            window.ReachesSourceEnd ? capture.MissingFinalStatus : false);
    }

    private static SliceWindow CreateWindow(double startSeconds, double? endSeconds, double originalDurationSeconds, ushort sampleRate)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Slice start must be finite and non-negative.");
        }

        if (endSeconds is not null && (!double.IsFinite(endSeconds.Value) || endSeconds.Value <= startSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Slice end must be finite and greater than the start.");
        }

        if (!double.IsFinite(originalDurationSeconds) || originalDurationSeconds <= 0)
        {
            throw new ArgumentException("Source duration must be positive and finite.", nameof(originalDurationSeconds));
        }

        if (sampleRate == 0)
        {
            throw new ArgumentException("Sample rate must be positive.", nameof(sampleRate));
        }

        var effectiveEnd = Math.Min(endSeconds ?? originalDurationSeconds, originalDurationSeconds);
        if (startSeconds >= effectiveEnd)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Slice start must be before the source end.");
        }

        var sourceSampleCount = RoundSamples(originalDurationSeconds, sampleRate);
        var startIndex = Math.Clamp(RoundSamples(startSeconds, sampleRate), 0, sourceSampleCount);
        var endIndexExclusive = Math.Clamp(RoundSamples(effectiveEnd, sampleRate), startIndex + 1, sourceSampleCount);
        var startUs = RoundMicroseconds(startSeconds);
        var endUs = RoundMicroseconds(effectiveEnd);
        var sourceEndUs = RoundMicroseconds(originalDurationSeconds);

        return new SliceWindow(
            startSeconds,
            effectiveEnd,
            startIndex,
            endIndexExclusive,
            startUs,
            endUs,
            effectiveEnd >= originalDurationSeconds || endUs >= sourceEndUs);
    }

    private static double GetRawDurationSeconds(RawTelemetryData raw)
    {
        if (raw.RecordingDurationSeconds is { } duration && duration > 0)
        {
            return duration;
        }

        var sampleCount = Math.Max(raw.Front.Length, raw.Rear.Length);
        sampleCount = Math.Max(sampleCount, MaxSegmentEndIndex(raw.FrontSegments));
        sampleCount = Math.Max(sampleCount, MaxSegmentEndIndex(raw.RearSegments));
        if (sampleCount > 0 && raw.SampleRate > 0)
        {
            return sampleCount / (double)raw.SampleRate;
        }

        throw new ArgumentException("Raw telemetry duration could not be determined.", nameof(raw));
    }

    private static int MaxSegmentEndIndex(RawCountSegment[] segments)
    {
        ulong max = 0;
        foreach (var segment in segments)
        {
            max = Math.Max(max, checked(segment.FirstIndex + (ulong)segment.Counts.Length));
        }

        return checked((int)Math.Min(max, int.MaxValue));
    }

    private static ushort[] SliceDense(ushort[] samples, SliceWindow window)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        var start = Math.Clamp(window.StartIndex, 0, samples.Length);
        var end = Math.Clamp(window.EndIndexExclusive, start, samples.Length);
        return samples[start..end];
    }

    private static RawCountSegment[] SliceCountSegments(RawCountSegment[] segments, SliceWindow window, ushort sampleRate)
    {
        if (segments.Length == 0)
        {
            return [];
        }

        var sliced = new List<RawCountSegment>(segments.Length);
        foreach (var segment in segments.OrderBy(segment => segment.FirstIndex))
        {
            var segmentStart = checked((int)Math.Min(segment.FirstIndex, int.MaxValue));
            var segmentEnd = checked((int)Math.Min(segment.FirstIndex + (ulong)segment.Counts.Length, int.MaxValue));
            var keepStart = Math.Max(segmentStart, window.StartIndex);
            var keepEnd = Math.Min(segmentEnd, window.EndIndexExclusive);
            if (keepEnd <= keepStart)
            {
                continue;
            }

            var offset = keepStart - segmentStart;
            sliced.Add(new RawCountSegment
            {
                FirstIndex = checked((ulong)(keepStart - window.StartIndex)),
                FirstMonotonicDeltaUs = RebaseMonotonicUs(segment.FirstMonotonicDeltaUs, offset, sampleRate, window.StartUs),
                Counts = segment.Counts[offset..(offset + keepEnd - keepStart)],
            });
        }

        return [.. sliced];
    }

    private static ushort[] FlattenCounts(RawCountSegment[] segments) =>
        segments.SelectMany(segment => segment.Counts).ToArray();

    private static RawStreamGap[] SliceStreamGaps(RawStreamGap[] gaps, SliceWindow window, int? imuSampleRate)
    {
        if (gaps.Length == 0)
        {
            return [];
        }

        var sliced = new List<RawStreamGap>(gaps.Length);
        foreach (var gap in gaps)
        {
            if (gap.StreamKind == SstV5Constants.StreamTravel)
            {
                AddSlicedFixedRateGap(sliced, gap, window.StartIndex, window.EndIndexExclusive, null, gap.MissingTimeUs.HasValue);
                continue;
            }

            if (gap.StreamKind == SstV5Constants.StreamImu && imuSampleRate is > 0)
            {
                var imuStart = RoundSamples(window.StartSeconds, imuSampleRate.Value);
                var imuEnd = RoundSamples(window.EndSeconds, imuSampleRate.Value);
                AddSlicedFixedRateGap(sliced, gap, imuStart, imuEnd, imuSampleRate.Value, gap.MissingTimeUs.HasValue);
                continue;
            }

            sliced.Add(CloneGap(gap));
        }

        return [.. sliced];
    }

    private static void AddSlicedFixedRateGap(
        List<RawStreamGap> gaps,
        RawStreamGap gap,
        int startIndex,
        int endIndexExclusive,
        int? sampleRate,
        bool hasMissingTime)
    {
        var gapStart = checked((int)Math.Min(gap.FirstMissingIndex, int.MaxValue));
        var gapEnd = checked((int)Math.Min(gap.FirstMissingIndex + gap.MissingCount, int.MaxValue));
        var keepStart = Math.Max(gapStart, startIndex);
        var keepEnd = Math.Min(gapEnd, endIndexExclusive);
        if (keepEnd <= keepStart)
        {
            return;
        }

        var missingCount = checked((ulong)(keepEnd - keepStart));
        gaps.Add(new RawStreamGap
        {
            StreamKind = gap.StreamKind,
            LocationId = gap.LocationId,
            FirstMissingIndex = checked((ulong)(keepStart - startIndex)),
            MissingCount = missingCount,
            MissingTimeUs = hasMissingTime && sampleRate is > 0
                ? checked((ulong)RoundMicroseconds(missingCount / (double)sampleRate.Value))
                : gap.MissingTimeUs,
            Reason = gap.Reason,
        });
    }

    private static RawStreamGap CloneGap(RawStreamGap gap) => new()
    {
        StreamKind = gap.StreamKind,
        LocationId = gap.LocationId,
        FirstMissingIndex = gap.FirstMissingIndex,
        MissingCount = gap.MissingCount,
        MissingTimeUs = gap.MissingTimeUs,
        Reason = gap.Reason,
    };

    private static RawImuData? SliceImuData(RawImuData? imuData, SliceWindow window, RawStreamGap[] streamGaps)
    {
        if (imuData is null)
        {
            return null;
        }

        var result = new RawImuData
        {
            Meta = [.. imuData.Meta],
            SampleRate = imuData.SampleRate,
            ActiveLocations = [.. imuData.ActiveLocations],
            HasGaps = imuData.HasGaps,
        };

        if (imuData.Segments.Count > 0 && imuData.SampleRate > 0)
        {
            result.Segments = SliceImuSegments(imuData.Segments, window, imuData.SampleRate);
            RawImuDataSegmentHelper.PopulateDenseRecordsFromAlignedSegments(result, streamGaps);
            return result;
        }

        if (imuData.Records.Count > 0 && imuData.SampleRate > 0 && imuData.ActiveLocations.Count > 0)
        {
            var startSample = RoundSamples(window.StartSeconds, imuData.SampleRate);
            var endSample = RoundSamples(window.EndSeconds, imuData.SampleRate);
            var locations = imuData.ActiveLocations.Count;
            var startRecord = Math.Clamp(startSample * locations, 0, imuData.Records.Count);
            var endRecord = Math.Clamp(endSample * locations, startRecord, imuData.Records.Count);
            result.Records = imuData.Records.GetRange(startRecord, endRecord - startRecord);
        }

        return result;
    }

    private static List<RawImuSegment> SliceImuSegments(List<RawImuSegment> segments, SliceWindow window, int sampleRate)
    {
        var startIndex = RoundSamples(window.StartSeconds, sampleRate);
        var endIndexExclusive = RoundSamples(window.EndSeconds, sampleRate);
        var sliced = new List<RawImuSegment>(segments.Count);
        foreach (var segment in segments.OrderBy(segment => segment.FirstIndex))
        {
            var segmentStart = checked((int)Math.Min(segment.FirstIndex, int.MaxValue));
            var segmentEnd = checked((int)Math.Min(segment.FirstIndex + (ulong)segment.Records.Length, int.MaxValue));
            var keepStart = Math.Max(segmentStart, startIndex);
            var keepEnd = Math.Min(segmentEnd, endIndexExclusive);
            if (keepEnd <= keepStart)
            {
                continue;
            }

            var offset = keepStart - segmentStart;
            sliced.Add(new RawImuSegment
            {
                LocationId = segment.LocationId,
                FirstIndex = checked((ulong)(keepStart - startIndex)),
                FirstMonotonicDeltaUs = RebaseMonotonicUs(segment.FirstMonotonicDeltaUs, offset, sampleRate, window.StartUs),
                Records = segment.Records[offset..(offset + keepEnd - keepStart)],
            });
        }

        return sliced;
    }

    private static MarkerData[] SliceMarkers(MarkerData[] markers, SliceWindow window) =>
        markers
            .Where(marker => marker.TimestampOffset >= window.StartSeconds && marker.TimestampOffset < window.EndSeconds)
            .Select(marker => marker with { TimestampOffset = marker.TimestampOffset - window.StartSeconds })
            .ToArray();

    private static GpsRecord[]? SliceGpsData(GpsRecord[]? records, long baseUtcMs, SliceWindow window)
    {
        if (records is null || records.Length == 0)
        {
            return null;
        }

        var startUtcMs = checked(baseUtcMs + RoundMilliseconds(window.StartSeconds));
        var endUtcMs = checked(baseUtcMs + RoundMilliseconds(window.EndSeconds));
        var sliced = records
            .Where(record =>
            {
                var utcMs = new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds();
                return utcMs >= startUtcMs && utcMs < endUtcMs;
            })
            .ToArray();
        return sliced.Length == 0 ? null : sliced;
    }

    private static TemperatureSample[] SliceTemperatureData(
        TemperatureSample[] samples,
        long baseUtcSeconds,
        SliceWindow window)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        var startUtcSeconds = checked(baseUtcSeconds + (long)Math.Floor(window.StartSeconds));
        var endUtcSeconds = checked(baseUtcSeconds + (long)Math.Ceiling(window.EndSeconds));
        return samples
            .Where(sample => sample.TimestampUtc >= startUtcSeconds && sample.TimestampUtc < endUtcSeconds)
            .ToArray();
    }

    private static SstFinalStatus? SliceFinalStatus(SstFinalStatus? finalStatus, SliceWindow window, double originalDurationSeconds)
    {
        if (finalStatus is null || !window.ReachesSourceEnd)
        {
            return null;
        }

        return new SstFinalStatus
        {
            SessionResultReason = finalStatus.SessionResultReason,
            StoppedMonotonicDeltaUs = SubtractClamped(finalStatus.StoppedMonotonicDeltaUs, window.StartUs),
            Streams = finalStatus.Streams
                .Select(stream => new SstStreamFinalStatus
                {
                    StreamKind = stream.StreamKind,
                    ProducerState = stream.ProducerState,
                    ProducerFailureReason = stream.ProducerFailureReason,
                    SinkBacklogBatches = stream.SinkBacklogBatches,
                    ProducerMissedCount = stream.ProducerMissedCount,
                    ProducerMissingTimeUs = stream.ProducerMissingTimeUs,
                    SinkMissedCount = stream.SinkMissedCount,
                    SinkMissingTimeUs = stream.SinkMissingTimeUs,
                })
                .ToArray(),
        };
    }

    private static long RawBaseUtcMs(RawTelemetryData raw) =>
        raw.SessionStartUtcMs != 0 ? raw.SessionStartUtcMs : checked(raw.Timestamp * 1000);

    private static long RawBaseUtcSeconds(RawTelemetryData raw) =>
        raw.SessionStartUtcMs != 0 ? raw.SessionStartUtcMs / 1000 : raw.Timestamp;

    private static ulong RebaseMonotonicUs(ulong firstMonotonicDeltaUs, int sampleOffset, int sampleRate, ulong startUs)
    {
        var sampleOffsetUs = RoundMicroseconds(sampleOffset / (double)sampleRate);
        return SubtractClamped(checked(firstMonotonicDeltaUs + sampleOffsetUs), startUs);
    }

    private static ulong SubtractClamped(ulong value, ulong subtract) =>
        value > subtract ? value - subtract : 0;

    private static int RoundSamples(double seconds, int sampleRate) =>
        checked((int)Math.Round(seconds * sampleRate, MidpointRounding.AwayFromZero));

    private static ulong RoundMicroseconds(double seconds) =>
        checked((ulong)Math.Round(seconds * 1_000_000.0, MidpointRounding.AwayFromZero));

    private static long RoundMilliseconds(double seconds) =>
        checked((long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero));

    private sealed record SliceWindow(
        double StartSeconds,
        double EndSeconds,
        int StartIndex,
        int EndIndexExclusive,
        ulong StartUs,
        ulong EndUs,
        bool ReachesSourceEnd)
    {
        public double DurationSeconds => EndSeconds - StartSeconds;
    }
}
