using System;

namespace Sufni.Telemetry.Tests;

public class TelemetryDataLiveCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FromLiveCapture_PublishesSnapshotIsolatedFromMutableInput(bool segmentedImu)
    {
        var front = Enumerable.Repeat((ushort)1200, 80).ToArray();
        var rear = Enumerable.Repeat((ushort)1300, 80).ToArray();
        ImuRecord[] imuSamples = [new ImuRecord(0, 0, 16384, 0, 0, 0)];
        var imuData = new RawImuData
        {
            SampleRate = 100,
            ActiveLocations = [0],
            Meta = [new ImuMetaEntry(0, 16384f, 131f)],
            Records = segmentedImu ? [] : [.. imuSamples],
            Segments = segmentedImu ? [new RawImuSegment { LocationId = 0, Records = [.. imuSamples] }] : [],
        };
        GpsRecord[] gpsData =
        [
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                Latitude: 42.6977,
                Longitude: 23.3219,
                Altitude: 600,
                Speed: 10,
                Heading: 90,
                FixMode: 3,
                Satellites: 12,
                Epe2d: 0.5f,
                Epe3d: 0.8f),
        ];
        MarkerData[] markers = [new MarkerData(0.5)];

        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 4,
                SampleRate = 200,
                Timestamp = 1_704_164_646,
                Duration = 0.4,
            },
            BikeData: CreateBikeData(),
            FrontSegments: [new RawCountSegment { Counts = front }],
            RearSegments: [new RawCountSegment { Counts = rear }],
            ImuData: imuData,
            GpsData: gpsData,
            Markers: markers,
            StreamGaps: [],
            FinalStatus: null,
            MissingFinalStatus: false);

        var result = TelemetryData.FromLiveCapture(capture);

        Assert.Equal(gpsData, result.GpsData!.Value);
        Assert.Equal(markers, result.Markers);
        Assert.All(result.Front.Travel, value => Assert.Equal(120, value));
        Assert.All(result.Rear.Travel, value => Assert.Equal(130, value));
        var frozen = result.BinaryForm;
        gpsData[0] = gpsData[0] with { Latitude = 0 };
        markers[0] = new MarkerData(99);
        front[0] = 0;
        rear[0] = 0;
        Assert.Equal(frozen, result.BinaryForm);
    }

    [Fact]
    public void FromLiveCapture_WithShortCapture_MarksSuspensionAbsent()
    {
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 4,
                SampleRate = 200,
                Timestamp = 1_704_164_646,
                Duration = 0.02,
            },
            BikeData: CreateBikeData(),
            FrontSegments: [new RawCountSegment { Counts = [1000, 1010, 1020, 1030] }],
            RearSegments: [new RawCountSegment { Counts = [1100, 1110, 1120, 1130] }],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps: [],
            FinalStatus: null,
            MissingFinalStatus: false);

        var result = TelemetryData.FromLiveCapture(capture);

        Assert.False(result.Front.Present);
        Assert.False(result.Rear.Present);
    }

    [Fact]
    public void FromLiveCapture_WithSegmentedTravel_PreservesGaps()
    {
        var finalStatus = new SstFinalStatus
        {
            SessionResultReason = 1,
            StoppedMonotonicDeltaUs = 700_000,
            Streams =
            [
                new SstStreamFinalStatus
                {
                    StreamKind = SstV5ProtocolConstants.StreamTravel,
                    ProducerState = 1,
                }
            ],
        };
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_704_164_646,
                Duration = 0.7,
            },
            BikeData: CreateBikeData(),
            FrontSegments:
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = Enumerable.Range(0, 32).Select(index => (ushort)(1000 + index)).ToArray(),
                },
                new RawCountSegment
                {
                    FirstIndex = 36,
                    FirstMonotonicDeltaUs = 360_000,
                    Counts = Enumerable.Range(0, 32).Select(index => (ushort)(1100 + index)).ToArray(),
                },
            ],
            RearSegments: [],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps:
            [
                new RawStreamGap
                {
                    StreamKind = SstV5ProtocolConstants.StreamTravel,
                    LocationId = (byte)SstV5ProtocolConstants.SensorForkTravel,
                    FirstMissingIndex = 32,
                    MissingCount = 4,
                    MissingTimeUs = 40_000,
                    Reason = "index_gap",
                },
            ],
            FinalStatus: finalStatus,
            MissingFinalStatus: false);

        var result = TelemetryData.FromLiveCapture(capture);

        Assert.Equal(4, result.Metadata.Version);
        Assert.True(result.Front.Present);
        Assert.True(result.Front.HasGaps);
        Assert.False(result.Rear.Present);
        Assert.Equal([0, 32], result.Front.Segments.Select(segment => segment.FirstDenseIndex));
        Assert.Equal([0, 0.36], result.Front.Segments.Select(segment => segment.StartSeconds));
        Assert.All(result.Front.Segments, segment => Assert.Equal(32, segment.SampleCount));
        Assert.Equal(
            Enumerable.Range(1000, 32).Concat(Enumerable.Range(1100, 32)).Select(value => value * 0.1),
            result.Front.Travel);
        Assert.All(result.Front.Velocity, value => Assert.Equal(10, value, 6));
    }

    private static BikeData CreateBikeData(double measurementScale = 0.1)
    {
        return new BikeData(
            FrontMaxTravel: 180,
            RearMaxTravel: 170,
            FrontMeasurementToTravel: measurement => measurement * measurementScale,
            RearMeasurementToTravel: measurement => measurement * measurementScale);
    }
}
