namespace Sufni.Telemetry;

internal static class TelemetrySlicer
{
    private const double SampleIndexTolerance = 1e-9;

    public static RawTelemetryData Slice(RawTelemetryData raw, double startSeconds, double? endSeconds)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (raw.SampleRate == 0)
        {
            throw new ArgumentException("Sample rate must be positive.", nameof(raw));
        }

        var window = SliceWindow.Normalize(startSeconds, endSeconds, GetRawDurationSeconds(raw));
        var streamGaps = SliceStreamGaps(raw.StreamGaps, window, raw.SampleRate, raw.ImuData?.SampleRate);
        var frontSegments = SliceCountSegments(raw.FrontSegments, window, raw.SampleRate);
        var rearSegments = SliceCountSegments(raw.RearSegments, window, raw.SampleRate);

        return new RawTelemetryData
        {
            Magic = raw.Magic?.ToArray() ?? [],
            Version = raw.Version,
            SampleRate = raw.SampleRate,
            Timestamp = checked(raw.Timestamp + WholeSecondOffset(window.StartSeconds)),
            Front = frontSegments.Length > 0 ? FlattenCounts(frontSegments) : SliceDense(raw.Front, window, raw.SampleRate),
            Rear = rearSegments.Length > 0 ? FlattenCounts(rearSegments) : SliceDense(raw.Rear, window, raw.SampleRate),
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
                : checked(raw.SessionStartUtcMs + TimeOffsetMilliseconds(window.StartSeconds)),
            RecordingDurationSeconds = window.DurationSeconds,
            FrontSegments = frontSegments,
            RearSegments = rearSegments,
            StreamGaps = streamGaps,
            FinalStatus = SliceFinalStatus(raw.FinalStatus, window),
            MissingFinalStatus = window.ReachesSourceEnd ? raw.MissingFinalStatus : false,
        };
    }

    public static LiveTelemetryCapture Slice(LiveTelemetryCapture capture, double startSeconds, double? endSeconds)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var sampleRate = checked((ushort)Math.Clamp(capture.Metadata.SampleRate, 0, ushort.MaxValue));
        if (sampleRate == 0)
        {
            throw new ArgumentException("Sample rate must be positive.", nameof(capture));
        }

        var window = SliceWindow.Normalize(startSeconds, endSeconds, capture.Metadata.Duration);
        var streamGaps = SliceStreamGaps(capture.StreamGaps, window, sampleRate, capture.ImuData?.SampleRate);
        var frontSegments = SliceCountSegments(capture.FrontSegments, window, sampleRate);
        var rearSegments = SliceCountSegments(capture.RearSegments, window, sampleRate);
        var metadata = new Metadata
        {
            SourceName = capture.Metadata.SourceName,
            Version = capture.Metadata.Version,
            SampleRate = capture.Metadata.SampleRate,
            Timestamp = checked(capture.Metadata.Timestamp + WholeSecondOffset(window.StartSeconds)),
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
            SliceFinalStatus(capture.FinalStatus, window),
            window.ReachesSourceEnd ? capture.MissingFinalStatus : false)
        {
            TemperatureData = SliceTemperatureData(
                capture.TemperatureData,
                capture.Metadata.Timestamp,
                window),
        };
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

    private static int MaxSegmentEndIndex(List<RawImuSegment> segments)
    {
        ulong max = 0;
        foreach (var segment in segments)
        {
            max = Math.Max(max, checked(segment.FirstIndex + (ulong)segment.Records.Length));
        }

        return checked((int)Math.Min(max, int.MaxValue));
    }

    private static ushort[] SliceDense(ushort[] samples, SliceWindow window, ushort sampleRate)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        var start = Math.Clamp(StartSampleIndex(window.StartSeconds, sampleRate), 0, samples.Length);
        var end = EndSampleIndexExclusive(window, sampleRate, samples.Length);
        end = Math.Clamp(end, start, samples.Length);
        return samples[start..end];
    }

    private static RawCountSegment[] SliceCountSegments(RawCountSegment[] segments, SliceWindow window, ushort sampleRate)
    {
        if (segments.Length == 0)
        {
            return [];
        }

        var startIndex = Math.Clamp(StartSampleIndex(window.StartSeconds, sampleRate), 0, MaxSegmentEndIndex(segments));
        var endIndexExclusive = EndSampleIndexExclusive(window, sampleRate, MaxSegmentEndIndex(segments));
        endIndexExclusive = Math.Max(endIndexExclusive, startIndex);
        var sliced = new List<RawCountSegment>(segments.Length);
        foreach (var segment in segments.OrderBy(segment => segment.FirstIndex))
        {
            var segmentStart = checked((int)Math.Min(segment.FirstIndex, int.MaxValue));
            var segmentEnd = checked((int)Math.Min(segment.FirstIndex + (ulong)segment.Counts.Length, int.MaxValue));
            var keepStart = Math.Max(segmentStart, startIndex);
            var keepEnd = Math.Min(segmentEnd, endIndexExclusive);
            if (keepEnd <= keepStart)
            {
                continue;
            }

            var offset = keepStart - segmentStart;
            sliced.Add(new RawCountSegment
            {
                FirstIndex = checked((ulong)(keepStart - startIndex)),
                FirstMonotonicDeltaUs = RebaseMonotonicUs(segment.FirstMonotonicDeltaUs, offset, sampleRate, window.StartUs),
                Counts = segment.Counts[offset..(offset + keepEnd - keepStart)],
            });
        }

        return [.. sliced];
    }

    private static ushort[] FlattenCounts(RawCountSegment[] segments) =>
        segments.SelectMany(segment => segment.Counts).ToArray();

    private static RawStreamGap[] SliceStreamGaps(
        RawStreamGap[] gaps,
        SliceWindow window,
        int travelSampleRate,
        int? imuSampleRate)
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
                var start = StartSampleIndex(window.StartSeconds, travelSampleRate);
                var end = EndSampleIndexExclusive(window, travelSampleRate, int.MaxValue);
                AddSlicedFixedRateGap(sliced, gap, start, end, travelSampleRate, gap.MissingTimeUs.HasValue);
                continue;
            }

            if (gap.StreamKind == SstV5Constants.StreamImu && imuSampleRate is > 0)
            {
                var start = StartSampleIndex(window.StartSeconds, imuSampleRate.Value);
                var end = EndSampleIndexExclusive(window, imuSampleRate.Value, int.MaxValue);
                AddSlicedFixedRateGap(sliced, gap, start, end, imuSampleRate.Value, gap.MissingTimeUs.HasValue);
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
                ? checked((ulong)TimeOffsetMicroseconds(missingCount / (double)sampleRate.Value))
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
            RawImuDataSegmentHelper.FinalizeCanonicalSegments(result, streamGaps);
            return result;
        }

        if (imuData.Records.Count > 0 && imuData.SampleRate > 0 && imuData.ActiveLocations.Count > 0)
        {
            var locations = imuData.ActiveLocations.Count;
            var sampleCount = imuData.Records.Count / locations;
            var startSample = Math.Clamp(StartSampleIndex(window.StartSeconds, imuData.SampleRate), 0, sampleCount);
            var endSample = EndSampleIndexExclusive(window, imuData.SampleRate, sampleCount);
            endSample = Math.Clamp(endSample, startSample, sampleCount);
            var startRecord = startSample * locations;
            var endRecord = endSample * locations;
            result.Records = imuData.Records.GetRange(startRecord, endRecord - startRecord);
        }

        return result;
    }

    private static List<RawImuSegment> SliceImuSegments(List<RawImuSegment> segments, SliceWindow window, int sampleRate)
    {
        var startIndex = Math.Clamp(StartSampleIndex(window.StartSeconds, sampleRate), 0, MaxSegmentEndIndex(segments));
        var endIndexExclusive = EndSampleIndexExclusive(window, sampleRate, MaxSegmentEndIndex(segments));
        endIndexExclusive = Math.Max(endIndexExclusive, startIndex);
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
            .Where(marker => ContainsTimestamp(marker.TimestampOffset, window))
            .Select(marker => marker with { TimestampOffset = marker.TimestampOffset - window.StartSeconds })
            .ToArray();

    private static GpsRecord[]? SliceGpsData(GpsRecord[]? records, long baseUtcMs, SliceWindow window)
    {
        if (records is null || records.Length == 0)
        {
            return null;
        }

        var sliced = records
            .Where(record =>
            {
                var utcMs = new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds();
                var sourceSeconds = (utcMs - baseUtcMs) / 1000.0;
                return ContainsTimestamp(sourceSeconds, window);
            })
            .ToArray();
        return sliced.Length == 0 ? null : sliced;
    }

    private static TemperatureSample[] SliceTemperatureData(
        TemperatureSample[] samples,
        double baseUtcSeconds,
        SliceWindow window)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        return samples
            .Where(sample => ContainsTimestamp(sample.TimestampUtc - baseUtcSeconds, window))
            .ToArray();
    }

    private static SstFinalStatus? SliceFinalStatus(SstFinalStatus? finalStatus, SliceWindow window)
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

    private static double RawBaseUtcSeconds(RawTelemetryData raw) =>
        raw.SessionStartUtcMs != 0 ? raw.SessionStartUtcMs / 1000.0 : raw.Timestamp;

    private static ulong RebaseMonotonicUs(ulong firstMonotonicDeltaUs, int sampleOffset, int sampleRate, ulong startUs)
    {
        var sampleOffsetUs = TimeOffsetMicroseconds(sampleOffset / (double)sampleRate);
        return SubtractClamped(checked(firstMonotonicDeltaUs + sampleOffsetUs), startUs);
    }

    private static ulong SubtractClamped(ulong value, ulong subtract) =>
        value > subtract ? value - subtract : 0;

    private static bool ContainsTimestamp(double timestampSeconds, SliceWindow window) =>
        timestampSeconds >= window.StartSeconds && timestampSeconds < window.EndSeconds;

    private static int StartSampleIndex(double startSeconds, double sampleRateHz) =>
        checked((int)Math.Ceiling(SnapSampleIndex(startSeconds * sampleRateHz)));

    private static int EndSampleIndexExclusive(SliceWindow window, double sampleRateHz, int count) =>
        window.IsOpenEnded
            ? count
            : EndSampleIndexExclusive(window.EndSeconds, sampleRateHz, count);

    private static int EndSampleIndexExclusive(double endSeconds, double sampleRateHz, int count)
    {
        var index = checked((int)Math.Ceiling(SnapSampleIndex(endSeconds * sampleRateHz)));
        return Math.Clamp(index, 0, count);
    }

    private static double SnapSampleIndex(double value)
    {
        var nearest = Math.Round(value, MidpointRounding.AwayFromZero);
        var tolerance = SampleIndexTolerance * Math.Max(1.0, Math.Abs(value));
        return Math.Abs(value - nearest) <= tolerance ? nearest : value;
    }

    private static ulong TimeOffsetMicroseconds(double seconds) =>
        checked((ulong)Math.Round(seconds * 1_000_000.0, MidpointRounding.AwayFromZero));

    private static long TimeOffsetMilliseconds(double seconds) =>
        checked((long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero));

    private static long WholeSecondOffset(double seconds) =>
        checked((long)Math.Floor(seconds));

    private sealed record SliceWindow(
        double StartSeconds,
        double EndSeconds,
        bool IsOpenEnded,
        ulong StartUs,
        bool ReachesSourceEnd)
    {
        public double DurationSeconds => EndSeconds - StartSeconds;

        public static SliceWindow Normalize(double startSeconds, double? endSeconds, double durationSeconds)
        {
            if (!double.IsFinite(startSeconds) || startSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startSeconds), "Slice start must be finite and non-negative.");
            }

            if (endSeconds is not null && (!double.IsFinite(endSeconds.Value) || endSeconds.Value <= startSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(endSeconds), "Slice end must be finite and greater than the start.");
            }

            if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            {
                throw new ArgumentException("Source duration must be positive and finite.", nameof(durationSeconds));
            }

            var effectiveEnd = Math.Min(endSeconds ?? durationSeconds, durationSeconds);
            if (startSeconds >= effectiveEnd)
            {
                throw new ArgumentOutOfRangeException(nameof(startSeconds), "Slice start must be before the source end.");
            }

            return new SliceWindow(
                startSeconds,
                effectiveEnd,
                endSeconds is null,
                TimeOffsetMicroseconds(startSeconds),
                endSeconds is null || effectiveEnd >= durationSeconds);
        }
    }
}
