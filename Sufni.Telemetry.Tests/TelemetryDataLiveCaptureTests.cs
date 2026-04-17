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

    private static BikeData CreateBikeData()
    {
        return new BikeData(
            HeadAngle: 63,
            FrontMaxTravel: 180,
            RearMaxTravel: 170,
            FrontMeasurementToTravel: measurement => measurement / 10.0,
            RearMeasurementToTravel: measurement => measurement / 10.0);
    }
}