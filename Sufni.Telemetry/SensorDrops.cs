namespace Sufni.Telemetry;

/// <summary>Removes the readings of dropped ranges from a recording before processing.</summary>
internal static class SensorDrops
{
    private const string DroppedReason = "dropped";

    /// <param name="drops">Drop decisions on the recording source timeline.</param>
    /// <param name="sourceStartSeconds">Where <paramref name="raw"/> starts on the recording source timeline.</param>
    public static RawTelemetryData Apply(RawTelemetryData raw, SensorDecisions drops, double sourceStartSeconds)
    {
        if (drops.IsEmpty) return raw;
        List<SourceTimeRange> Local(SensorIdentity sensor) => [.. drops.DropRanges(sensor).Select(range =>
            new SourceTimeRange(range.StartSeconds - sourceStartSeconds, range.EndSeconds - sourceStartSeconds))];

        var gaps = raw.StreamGaps.ToList();
        var front = Cut(raw.FrontSegments, raw.Front, raw.SampleRate, Local(new(SensorKind.ForkTravel)),
            SstV5ProtocolConstants.SensorForkTravel, gaps);
        var rear = Cut(raw.RearSegments, raw.Rear, raw.SampleRate, Local(new(SensorKind.ShockTravel)),
            SstV5ProtocolConstants.SensorShockTravel, gaps);
        var baseUtcMs = TelemetrySlicer.RawBaseUtcMs(raw);
        var gpsRanges = Local(new(SensorKind.Gps));
        var gps = raw.GpsData?.Where(record => !SourceTimeRanges.Contains(gpsRanges,
            (new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds() - baseUtcMs) / 1000.0)).ToArray();
        var baseSeconds = TelemetrySlicer.RawBaseUtcSeconds(raw);

        return new RawTelemetryData
        {
            Version = raw.Version,
            SampleRate = raw.SampleRate,
            Timestamp = raw.Timestamp,
            Front = front is null ? raw.Front : [.. front.SelectMany(segment => segment.Counts)],
            Rear = rear is null ? raw.Rear : [.. rear.SelectMany(segment => segment.Counts)],
            Markers = raw.Markers,
            ImuData = drops.Sensors.Any(list => list.Sensor.Kind == SensorKind.Imu)
                ? CutImu(raw.ImuData, sensor => Local(sensor))
                : raw.ImuData,
            GpsData = gps is { Length: > 0 } ? gps : null,
            TemperatureData = [.. raw.TemperatureData.Where(sample => !SourceTimeRanges.Contains(
                Local(new(SensorKind.Temperature, sample.LocationId)), sample.TimestampUtc - baseSeconds))],
            SessionStartUtcMs = raw.SessionStartUtcMs,
            RecordingDurationSeconds = raw.RecordingDurationSeconds,
            FrontSegments = front ?? DenseSegments(raw.FrontSegments, raw.Front),
            RearSegments = rear ?? DenseSegments(raw.RearSegments, raw.Rear),
            StreamGaps = [.. gaps],
            FinalStatus = raw.FinalStatus,
            MissingFinalStatus = raw.MissingFinalStatus,
        };
    }

    /// <summary>The segments of one travel side outside <paramref name="ranges"/>, or null when nothing is cut.
    /// Each cut adds a travel gap of that side.</summary>
    private static RawCountSegment[]? Cut(RawCountSegment[] segments, ushort[] dense, int rate,
        List<SourceTimeRange> ranges, uint sensorBit, List<RawStreamGap> gaps)
    {
        if (ranges.Count == 0 || rate <= 0) return null;
        segments = DenseSegments(segments, dense);
        var result = new List<RawCountSegment>();
        var cut = false;
        foreach (var segment in segments)
        {
            var start = segment.FirstMonotonicDeltaUs / 1e6;
            var next = 0;
            foreach (var (first, end) in KeptRuns(start, segment.Counts.Length, rate, ranges))
            {
                if (first > next) AddGap(segment.FirstIndex + (ulong)next, first - next);
                result.Add(new RawCountSegment
                {
                    FirstIndex = segment.FirstIndex + (ulong)first,
                    FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs + OffsetUs(first, rate),
                    Counts = segment.Counts[first..end],
                });
                next = end;
            }
            if (segment.Counts.Length > next) AddGap(segment.FirstIndex + (ulong)next, segment.Counts.Length - next);
        }
        return cut ? [.. result] : null;

        void AddGap(ulong firstMissingIndex, int missingCount)
        {
            cut = true;
            gaps.Add(new RawStreamGap
            {
                StreamKind = SstV5ProtocolConstants.StreamTravel,
                LocationId = (byte)sensorBit,
                FirstMissingIndex = firstMissingIndex,
                MissingCount = (ulong)missingCount,
                Reason = DroppedReason,
            });
        }
    }

    private static RawImuData? CutImu(RawImuData? imu, Func<SensorIdentity, List<SourceTimeRange>> rangesOf)
    {
        if (imu is not { SampleRate: > 0 }) return imu;
        var segments = new List<RawImuSegment>();
        var cut = false;
        foreach (var segment in imu.SampleSegments)
        {
            var ranges = rangesOf(new SensorIdentity(SensorKind.Imu, segment.LocationId));
            var records = new ImuRecord[segment.Count];
            for (var i = 0; i < records.Length; i++) records[i] = segment[i];
            var kept = 0;
            foreach (var (first, end) in KeptRuns(segment.FirstMonotonicDeltaUs / 1e6, segment.Count, imu.SampleRate, ranges))
            {
                segments.Add(new RawImuSegment
                {
                    LocationId = segment.LocationId,
                    FirstIndex = segment.FirstIndex + (ulong)first,
                    FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs + OffsetUs(first, imu.SampleRate),
                    Records = [.. records[first..end]],
                });
                kept += end - first;
            }
            cut |= kept < segment.Count;
        }
        return cut ? imu with { Records = [], Segments = [.. segments], HasGaps = true } : imu;
    }

    // A cut sends both sides through the segmented path, which reads only segments.
    private static RawCountSegment[] DenseSegments(RawCountSegment[] segments, ushort[] dense) =>
        segments.Length == 0 && dense.Length > 0 ? [new RawCountSegment { Counts = dense }] : segments;

    /// <summary>The index runs <c>[first, end)</c> of samples whose time is outside every range.</summary>
    private static IEnumerable<(int First, int End)> KeptRuns(double startSeconds, int count, double rate,
        List<SourceTimeRange> ranges)
    {
        var first = -1;
        for (var i = 0; i < count; i++)
        {
            var dropped = SourceTimeRanges.Contains(ranges, startSeconds + i / rate);
            if (!dropped && first < 0) first = i;
            if (dropped && first >= 0)
            {
                yield return (first, i);
                first = -1;
            }
        }
        if (first >= 0) yield return (first, count);
    }

    private static ulong OffsetUs(int samples, double rate) => (ulong)Math.Round(samples * 1e6 / rate);
}
