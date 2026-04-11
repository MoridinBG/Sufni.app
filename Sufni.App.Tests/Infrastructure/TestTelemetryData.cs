using System;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Infrastructure;

public static class TestTelemetryData
{
    public static TelemetryData Create(bool frontPresent = true, bool rearPresent = true)
    {
        const int sampleCount = 256;
        const int sampleRate = 1000;

        var front = frontPresent ? BuildWave(sampleCount, 0.0) : [];
        var rear = rearPresent ? BuildWave(sampleCount, Math.PI / 4.0) : [];

        var metadata = new Metadata
        {
            SourceName = "test.sst",
            Version = 3,
            SampleRate = sampleRate,
            Timestamp = 1_700_000_000,
            Duration = sampleCount / (double)sampleRate,
        };

        var bikeData = new BikeData(
            HeadAngle: 65.0,
            FrontMaxTravel: frontPresent ? 200.0 : null,
            RearMaxTravel: rearPresent ? 200.0 : null,
            FrontMeasurementToTravel: frontPresent ? MeasurementToTravel : null,
            RearMeasurementToTravel: rearPresent ? MeasurementToTravel : null);

        var rawData = new RawTelemetryData
        {
            Version = (byte)metadata.Version,
            SampleRate = (ushort)metadata.SampleRate,
            Timestamp = metadata.Timestamp,
            Front = front,
            Rear = rear,
            FrontAnomalyRate = 0.0,
            RearAnomalyRate = 0.0,
        };

        return TelemetryData.FromRecording(rawData, metadata, bikeData);
    }

    private static ushort[] BuildWave(int sampleCount, double phase)
    {
        return Enumerable.Range(0, sampleCount)
            .Select(index =>
            {
                var angle = phase + index * 2.0 * Math.PI / 48.0;
                var carrier = 1900.0 + 260.0 * Math.Sin(angle);
                var modulation = 55.0 * Math.Sin(angle * 0.35);
                var value = Math.Clamp(carrier + modulation, 1500.0, 2500.0);
                return (ushort)Math.Round(value);
            })
            .ToArray();
    }

    private static double MeasurementToTravel(ushort measurement)
    {
        return Math.Clamp((measurement - 1500.0) / 5.0, 0.0, 200.0);
    }
}