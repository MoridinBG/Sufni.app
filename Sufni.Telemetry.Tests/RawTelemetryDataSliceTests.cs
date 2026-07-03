using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class RawTelemetryDataSliceTests
{
    [Fact]
    public void Slice_DenseData_RebasesTimelineAndAuxiliaryData()
    {
        var raw = CreateRaw(sampleRate: 10, durationSeconds: 10);
        raw.Markers = [new MarkerData(1.5), new MarkerData(2.5), new MarkerData(7.9), new MarkerData(8.0)];
        raw.GpsData =
        [
            CreateGps(1_002),
            CreateGps(1_007),
            CreateGps(1_008),
        ];
        raw.TemperatureData =
        [
            new TemperatureSample(1_002, 0, 20),
            new TemperatureSample(1_007, 0, 21),
            new TemperatureSample(1_008, 0, 22),
        ];
        raw.FinalStatus = new SstFinalStatus
        {
            SessionResultReason = 2,
            StoppedMonotonicDeltaUs = 10_000_000,
        };
        raw.MissingFinalStatus = true;

        var result = raw.Slice(2.0, 8.0);

        Assert.Equal(1_002, result.Timestamp);
        Assert.Equal(1_002_000, result.SessionStartUtcMs);
        Assert.Equal(6.0, result.RecordingDurationSeconds);
        Assert.Equal(Enumerable.Range(20, 60).Select(index => (ushort)index), result.Front);
        Assert.Equal(Enumerable.Range(120, 60).Select(index => (ushort)index), result.Rear);
        Assert.Equal(Enumerable.Range(20, 60).Select(index => (ushort)index), result.FrontSegments.Single().Counts);
        Assert.Equal((ulong)0, result.FrontSegments.Single().FirstIndex);
        Assert.Equal((ulong)0, result.FrontSegments.Single().FirstMonotonicDeltaUs);
        Assert.Equal([new MarkerData(0.5), new MarkerData(5.9)], result.Markers);
        Assert.NotNull(result.GpsData);
        Assert.Equal([CreateGps(1_002), CreateGps(1_007)], result.GpsData);
        Assert.Equal(
            [new TemperatureSample(1_002, 0, 20), new TemperatureSample(1_007, 0, 21)],
            result.TemperatureData);
        Assert.Null(result.FinalStatus);
        Assert.False(result.MissingFinalStatus);
    }

    [Fact]
    public void Slice_ToSourceEnd_KeepsAndRebasesFinalStatus()
    {
        var raw = CreateRaw(sampleRate: 10, durationSeconds: 10);
        raw.FinalStatus = new SstFinalStatus
        {
            SessionResultReason = 4,
            StoppedMonotonicDeltaUs = 10_000_000,
            Streams =
            [
                new SstStreamFinalStatus
                {
                    StreamKind = SstV5ProtocolConstants.StreamTravel,
                    ProducerState = 3,
                    SinkMissedCount = 7,
                }
            ],
        };
        raw.MissingFinalStatus = true;

        var result = raw.Slice(2.5, null);

        Assert.Equal(1_002, result.Timestamp);
        Assert.Equal(1_002_500, result.SessionStartUtcMs);
        Assert.Equal(7.5, result.RecordingDurationSeconds);
        Assert.NotNull(result.FinalStatus);
        Assert.Equal((ulong)7_500_000, result.FinalStatus.StoppedMonotonicDeltaUs);
        Assert.Equal(4, result.FinalStatus.SessionResultReason);
        Assert.Equal(7UL, result.FinalStatus.Streams.Single().SinkMissedCount);
        Assert.True(result.MissingFinalStatus);
    }

    [Fact]
    public void Slice_SegmentedTravel_RebasesSegmentsAndTravelGaps()
    {
        var raw = CreateRaw(sampleRate: 10, durationSeconds: 10);
        raw.FrontSegments =
        [
            new RawCountSegment
            {
                FirstIndex = 0,
                FirstMonotonicDeltaUs = 0,
                Counts = Enumerable.Range(0, 20).Select(index => (ushort)index).ToArray(),
            },
            new RawCountSegment
            {
                FirstIndex = 40,
                FirstMonotonicDeltaUs = 4_000_000,
                Counts = Enumerable.Range(40, 40).Select(index => (ushort)index).ToArray(),
            },
        ];
        raw.RearSegments = [];
        raw.Front = raw.FrontSegments.SelectMany(segment => segment.Counts).ToArray();
        raw.Rear = [];
        raw.StreamGaps =
        [
            new RawStreamGap
            {
                StreamKind = SstV5ProtocolConstants.StreamTravel,
                LocationId = (byte)SstV5ProtocolConstants.SensorForkTravel,
                FirstMissingIndex = 20,
                MissingCount = 20,
                MissingTimeUs = 2_000_000,
                Reason = "index_gap",
            },
        ];

        var result = raw.Slice(1.0, 6.0);

        Assert.Equal([.. Enumerable.Range(10, 10).Concat(Enumerable.Range(40, 20)).Select(index => (ushort)index)], result.Front);
        Assert.Equal(2, result.FrontSegments.Length);
        Assert.Equal((ulong)0, result.FrontSegments[0].FirstIndex);
        Assert.Equal((ulong)0, result.FrontSegments[0].FirstMonotonicDeltaUs);
        Assert.Equal(Enumerable.Range(10, 10).Select(index => (ushort)index), result.FrontSegments[0].Counts);
        Assert.Equal((ulong)30, result.FrontSegments[1].FirstIndex);
        Assert.Equal((ulong)3_000_000, result.FrontSegments[1].FirstMonotonicDeltaUs);
        Assert.Equal(Enumerable.Range(40, 20).Select(index => (ushort)index), result.FrontSegments[1].Counts);
        var gap = Assert.Single(result.StreamGaps);
        Assert.Equal((ulong)10, gap.FirstMissingIndex);
        Assert.Equal((ulong)20, gap.MissingCount);
        Assert.Equal((ulong)2_000_000, gap.MissingTimeUs);
    }

    [Fact]
    public void Slice_SegmentedImu_RebasesSegmentsAndDenseRecords()
    {
        var raw = CreateRaw(sampleRate: 10, durationSeconds: 4);
        raw.ImuData = new RawImuData
        {
            SampleRate = 5,
            ActiveLocations = [0, 1],
            Meta =
            [
                new ImuMetaEntry(0, 16384, 131),
                new ImuMetaEntry(1, 16384, 131),
            ],
            Segments =
            [
                CreateImuSegment(locationId: 0, firstIndex: 0, firstMonotonicDeltaUs: 0, firstValue: 0, count: 20),
                CreateImuSegment(locationId: 1, firstIndex: 0, firstMonotonicDeltaUs: 0, firstValue: 100, count: 20),
            ],
        };

        var result = raw.Slice(1.0, 3.0);

        Assert.NotNull(result.ImuData);
        Assert.Equal(2, result.ImuData.Segments.Count);
        Assert.All(result.ImuData.Segments, segment =>
        {
            Assert.Equal((ulong)0, segment.FirstIndex);
            Assert.Equal((ulong)0, segment.FirstMonotonicDeltaUs);
            Assert.Equal(10, segment.Records.Length);
        });
        Assert.Equal(CreateImuRecord(5), result.ImuData.Segments[0].Records[0]);
        Assert.Equal(CreateImuRecord(105), result.ImuData.Segments[1].Records[0]);
        Assert.Equal(20, result.ImuData.Records.Count);
        Assert.Equal(CreateImuRecord(5), result.ImuData.Records[0]);
        Assert.Equal(CreateImuRecord(105), result.ImuData.Records[1]);
    }

    [Fact]
    public void Slice_Throws_WhenStartIsOutsideEffectiveRange()
    {
        var raw = CreateRaw(sampleRate: 10, durationSeconds: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => raw.Slice(10, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => raw.Slice(3, 3));
    }

    private static RawTelemetryData CreateRaw(ushort sampleRate, double durationSeconds)
    {
        var sampleCount = (int)(sampleRate * durationSeconds);
        var front = Enumerable.Range(0, sampleCount).Select(index => (ushort)index).ToArray();
        var rear = Enumerable.Range(100, sampleCount).Select(index => (ushort)index).ToArray();
        return new RawTelemetryData
        {
            Magic = [(byte)'S', (byte)'S', (byte)'T', 5],
            Version = 5,
            SampleRate = sampleRate,
            Timestamp = 1_000,
            SessionStartUtcMs = 1_000_000,
            RecordingDurationSeconds = durationSeconds,
            Front = front,
            Rear = rear,
            FrontSegments =
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = front,
                }
            ],
            RearSegments =
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = rear,
                }
            ],
        };
    }

    private static GpsRecord CreateGps(long utcSeconds) => new(
        Timestamp: DateTimeOffset.FromUnixTimeSeconds(utcSeconds).UtcDateTime,
        Latitude: utcSeconds,
        Longitude: utcSeconds,
        Altitude: 100,
        Speed: 1,
        Heading: 2,
        FixMode: 3,
        Satellites: 4,
        Epe2d: 5,
        Epe3d: 6);

    private static RawImuSegment CreateImuSegment(byte locationId, ulong firstIndex, ulong firstMonotonicDeltaUs, int firstValue, int count) => new()
    {
        LocationId = locationId,
        FirstIndex = firstIndex,
        FirstMonotonicDeltaUs = firstMonotonicDeltaUs,
        Records = Enumerable.Range(firstValue, count).Select(CreateImuRecord).ToArray(),
    };

    private static ImuRecord CreateImuRecord(int value) => new(
        (short)value,
        (short)(value + 1),
        (short)(value + 2),
        (short)(value + 3),
        (short)(value + 4),
        (short)(value + 5));
}
