using System;

namespace Sufni.Telemetry.Tests;

public class TelemetryDataLiveCaptureTests
{
    [Fact]
    public void FromLiveCapture_PreservesAuxiliaryData_AndReportsDetectedAnomalies()
    {
        var front = BuildSignalWithSpike(1200, 80, spikeIndex: 32, spikeValue: 5000);
        var rear = BuildSignalWithSpike(1300, 80, spikeIndex: 48, spikeValue: 4800);
        var imuData = new RawImuData
        {
            SampleRate = 100,
            ActiveLocations = [0],
            Meta = [new ImuMetaEntry(0, 16384f, 131f)],
            Records = [new ImuRecord(0, 0, 16384, 0, 0, 0)],
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
            FrontMeasurements: front,
            RearMeasurements: rear,
            ImuData: imuData,
            GpsData: gpsData,
            Markers: markers);

        var result = TelemetryData.FromLiveCapture(capture);

        Assert.Same(imuData, result.ImuData);
        Assert.Same(gpsData, result.GpsData);
        Assert.Equal(markers, result.Markers);
        Assert.True(result.Front.Present);
        Assert.True(result.Rear.Present);
        Assert.True(result.Front.AnomalyRate > 0);
        Assert.True(result.Rear.AnomalyRate > 0);
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
            FrontMeasurements: [1000, 1010, 1020, 1030],
            RearMeasurements: [1100, 1110, 1120, 1130],
            ImuData: null,
            GpsData: null,
            Markers: []);

        var result = TelemetryData.FromLiveCapture(capture);

        Assert.False(result.Front.Present);
        Assert.False(result.Rear.Present);
    }

    [Fact]
    public void FromLiveCapture_WithSegmentedTravel_PreservesGapsAndFinalStatus()
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

        Assert.Equal(5, result.Metadata.Version);
        Assert.True(result.Front.Present);
        Assert.True(result.Front.HasGaps);
        Assert.False(result.Rear.Present);
        Assert.Single(result.StreamGaps);
        Assert.Same(finalStatus, result.FinalStatus);
        Assert.False(result.MissingFinalStatus);
    }

    [Fact]
    public void FromLiveCapture_MatchesFinalBatchProcessingForTheSameSegmentedRecording()
    {
        RawCountSegment[] frontSegments =
        [
            new RawCountSegment
            {
                FirstIndex = 0,
                FirstMonotonicDeltaUs = 0,
                Counts = Enumerable.Range(0, 40).Select(index => (ushort)(1000 + index)).ToArray(),
            },
            new RawCountSegment
            {
                FirstIndex = 44,
                FirstMonotonicDeltaUs = 440_000,
                Counts = Enumerable.Range(0, 40).Select(index => (ushort)(1100 + index)).ToArray(),
            },
        ];
        RawCountSegment[] rearSegments =
        [
            new RawCountSegment
            {
                FirstIndex = 0,
                FirstMonotonicDeltaUs = 0,
                Counts = Enumerable.Range(0, 84).Select(index => (ushort)(1200 + index % 9)).ToArray(),
            },
        ];
        RawStreamGap[] streamGaps =
        [
            new RawStreamGap
            {
                StreamKind = SstV5ProtocolConstants.StreamTravel,
                LocationId = (byte)SstV5ProtocolConstants.SensorForkTravel,
                FirstMissingIndex = 40,
                MissingCount = 4,
                MissingTimeUs = 40_000,
                Reason = "index_gap",
            },
        ];
        MarkerData[] markers = [new MarkerData(0.25), new MarkerData(0.6)];
        var finalStatus = new SstFinalStatus
        {
            SessionResultReason = 1,
            StoppedMonotonicDeltaUs = 840_000,
            Streams =
            [
                new SstStreamFinalStatus
                {
                    StreamKind = SstV5ProtocolConstants.StreamTravel,
                    ProducerState = 1,
                },
            ],
        };
        var bikeData = CreateBikeData();
        var metadata = new Metadata
        {
            SourceName = "parity-fixture",
            Version = 5,
            SampleRate = 100,
            Timestamp = 1_704_164_646,
            Duration = 0.84,
        };
        var capture = new LiveTelemetryCapture(
            metadata,
            bikeData,
            frontSegments,
            rearSegments,
            ImuData: null,
            GpsData: null,
            markers,
            streamGaps,
            finalStatus,
            MissingFinalStatus: false);
        var raw = new RawTelemetryData
        {
            Version = 5,
            SampleRate = 100,
            Timestamp = metadata.Timestamp,
            Front = frontSegments.SelectMany(segment => segment.Counts).ToArray(),
            Rear = rearSegments.SelectMany(segment => segment.Counts).ToArray(),
            FrontSegments = frontSegments,
            RearSegments = rearSegments,
            StreamGaps = streamGaps,
            FinalStatus = finalStatus,
            MissingFinalStatus = false,
            SessionStartUtcMs = metadata.Timestamp * 1000,
            RecordingDurationSeconds = metadata.Duration,
            Markers = markers,
        };

        var live = TelemetryData.FromLiveCapture(capture);
        var finalBatch = TelemetryData.FromRecording(raw, metadata, bikeData);

        Assert.Equal(finalBatch.BinaryForm, live.BinaryForm);
    }

    private static ushort[] BuildSignalWithSpike(ushort baseline, int length, int spikeIndex, ushort spikeValue)
    {
        var samples = new ushort[length];
        for (var index = 0; index < length; index++)
        {
            samples[index] = (ushort)(baseline + index % 7);
        }

        samples[spikeIndex] = spikeValue;
        return samples;
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
