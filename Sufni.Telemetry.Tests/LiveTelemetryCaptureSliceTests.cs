using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class LiveTelemetryCaptureSliceTests
{
    [Fact]
    public void Slice_RebasesTravelToSelectedTimeline()
    {
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 5,
                SampleRate = 10,
                Timestamp = 1_000,
                Duration = 10,
            },
            BikeData: CreateBikeData(),
            FrontSegments:
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = Enumerable.Range(0, 100).Select(index => (ushort)index).ToArray(),
                }
            ],
            RearSegments:
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = Enumerable.Range(100, 100).Select(index => (ushort)index).ToArray(),
                }
            ],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps: [],
            FinalStatus: null,
            MissingFinalStatus: false);

        var result = capture.Slice(2.0, 8.0);

        Assert.Equal(1_002, result.Metadata.Timestamp);
        Assert.Equal(6.0, result.Metadata.Duration);
        Assert.Equal(Enumerable.Range(20, 60).Select(index => (ushort)index), result.FrontMeasurements);
        Assert.Equal(Enumerable.Range(120, 60).Select(index => (ushort)index), result.RearMeasurements);
    }

    [Fact]
    public void Slice_ToSourceEnd_KeepsFinalStatus()
    {
        var finalStatus = new SstFinalStatus
        {
            SessionResultReason = 2,
            StoppedMonotonicDeltaUs = 10_000_000,
        };
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 5,
                SampleRate = 10,
                Timestamp = 1_000,
                Duration = 10,
            },
            BikeData: CreateBikeData(),
            FrontSegments: [new RawCountSegment { Counts = Enumerable.Range(0, 100).Select(index => (ushort)index).ToArray() }],
            RearSegments: [],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps: [],
            FinalStatus: finalStatus,
            MissingFinalStatus: true);

        var result = capture.Slice(2.5, null);

        Assert.Equal(1_002, result.Metadata.Timestamp);
        Assert.Equal(7.5, result.Metadata.Duration);
        Assert.NotNull(result.FinalStatus);
        Assert.Equal((ulong)7_500_000, result.FinalStatus.StoppedMonotonicDeltaUs);
        Assert.True(result.MissingFinalStatus);
    }

    [Fact]
    public void Slice_OpenEndedFractionalStart_UsesCeilStartAndKeepsFinalStatus()
    {
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 5,
                SampleRate = 4,
                Timestamp = 1_000,
                Duration = 5,
            },
            BikeData: CreateBikeData(),
            FrontSegments: [new RawCountSegment { Counts = Enumerable.Range(0, 20).Select(index => (ushort)index).ToArray() }],
            RearSegments: [new RawCountSegment { Counts = Enumerable.Range(100, 20).Select(index => (ushort)index).ToArray() }],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps: [],
            FinalStatus: new SstFinalStatus
            {
                StoppedMonotonicDeltaUs = 5_000_000,
            },
            MissingFinalStatus: true);

        var result = capture.Slice(2.6, null);

        Assert.Equal(1_002, result.Metadata.Timestamp);
        Assert.Equal(2.4, result.Metadata.Duration);
        Assert.Equal(Enumerable.Range(11, 9).Select(index => (ushort)index), result.FrontMeasurements);
        Assert.Equal(Enumerable.Range(111, 9).Select(index => (ushort)index), result.RearMeasurements);
        Assert.NotNull(result.FinalStatus);
        Assert.Equal((ulong)2_400_000, result.FinalStatus.StoppedMonotonicDeltaUs);
        Assert.True(result.MissingFinalStatus);
    }

    private static BikeData CreateBikeData() => new(
        FrontMaxTravel: 180,
        RearMaxTravel: 170,
        FrontMeasurementToTravel: measurement => measurement,
        RearMeasurementToTravel: measurement => measurement);

}
