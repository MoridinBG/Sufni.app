using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Infrastructure;

public static class TestTelemetryData
{
    public static TelemetryData CreateProcessed(bool frontPresent = true, bool rearPresent = true)
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

    public static TelemetryData CreateWithImu(
        IReadOnlyList<byte>? activeLocations = null,
        IReadOnlyList<ImuMetaEntry>? meta = null,
        IReadOnlyList<ImuRecord>? records = null,
        double duration = 2.0,
        int sampleRate = 100)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                Duration = duration,
                SampleRate = sampleRate,
            },
            Front = new Suspension
            {
                Present = true,
                Travel = [0.0],
                Velocity = [0.0],
                Strokes = new Strokes()
            },
            Rear = new Suspension
            {
                Present = true,
                Travel = [0.0],
                Velocity = [0.0],
                Strokes = new Strokes()
            },
            Airtimes = [],
            ImuData = new RawImuData
            {
                SampleRate = sampleRate,
                ActiveLocations = activeLocations?.ToList() ?? [0, 1],
                Meta = meta?.ToList() ??
                [
                    new ImuMetaEntry(0, 1.0f, 1.0f),
                    new ImuMetaEntry(1, 1.0f, 1.0f)
                ],
                Records = records?.ToList() ??
                [
                    new ImuRecord(1, 0, 1, 0, 0, 0),
                    new ImuRecord(2, 0, 1, 0, 0, 0),
                    new ImuRecord(3, 0, 1, 0, 0, 0),
                    new ImuRecord(4, 0, 1, 0, 0, 0)
                ]
            }
        };
    }

    public static TelemetryData CreateMinimal(
        double duration = 2.0,
        int sampleRate = 2)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                Duration = duration,
                SampleRate = sampleRate,
            },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 170.0,
                Travel = [0.0, 25.0, 50.0, 75.0],
                Velocity = [100.0, -50.0, 25.0, 0.0],
                Strokes = new Strokes()
            },
            Rear = new Suspension
            {
                Present = true,
                MaxTravel = 160.0,
                Travel = [0.0, 20.0, 40.0, 60.0],
                Velocity = [80.0, -40.0, 20.0, 0.0],
                Strokes = new Strokes()
            },
            Airtimes = []
        };
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
