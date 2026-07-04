using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class LiveTelemetryCaptureSliceTests
{
    [Fact]
    public void Slice_RebasesMetadataTravelAndAuxiliaryData()
    {
        var finalStatus = new SstFinalStatus
        {
            SessionResultReason = 1,
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
            GpsData:
            [
                CreateGps(1_002),
                CreateGps(1_007),
                CreateGps(1_008),
            ],
            Markers: [new MarkerData(1.5), new MarkerData(2.5), new MarkerData(7.5), new MarkerData(8.0)],
            StreamGaps: [],
            FinalStatus: finalStatus,
            MissingFinalStatus: true);

        var result = capture.Slice(2.0, 8.0);

        Assert.Equal("live", result.Metadata.SourceName);
        Assert.Equal(5, result.Metadata.Version);
        Assert.Equal(10, result.Metadata.SampleRate);
        Assert.Equal(1_002, result.Metadata.Timestamp);
        Assert.Equal(6.0, result.Metadata.Duration);
        Assert.Same(capture.BikeData, result.BikeData);
        Assert.Equal(Enumerable.Range(20, 60).Select(index => (ushort)index), result.FrontMeasurements);
        Assert.Equal(Enumerable.Range(120, 60).Select(index => (ushort)index), result.RearMeasurements);
        Assert.Equal([new MarkerData(0.5), new MarkerData(5.5)], result.Markers);
        Assert.NotNull(result.GpsData);
        Assert.Equal([CreateGps(1_002), CreateGps(1_007)], result.GpsData);
        Assert.Null(result.FinalStatus);
        Assert.False(result.MissingFinalStatus);
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
            FrontMeasurements: Enumerable.Range(0, 100).Select(index => (ushort)index).ToArray(),
            RearMeasurements: [],
            ImuData: null,
            GpsData: null,
            Markers: []) with
        {
            FinalStatus = finalStatus,
            MissingFinalStatus = true,
        };

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
            FrontMeasurements: Enumerable.Range(0, 20).Select(index => (ushort)index).ToArray(),
            RearMeasurements: Enumerable.Range(100, 20).Select(index => (ushort)index).ToArray(),
            ImuData: null,
            GpsData: null,
            Markers: []) with
        {
            FinalStatus = new SstFinalStatus
            {
                StoppedMonotonicDeltaUs = 5_000_000,
            },
            MissingFinalStatus = true,
        };

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
}
