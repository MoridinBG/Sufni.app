using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class TelemetryDataTests
{
    [Fact]
    public void FromRecording_WithV4Data_TransfersMarkersAndImuDataCorrectly()
    {
        // Arrange
        var front = new ushort[100];
        var rear = new ushort[100];

        // Create some movement (strokes)
        for (int i = 0; i < 50; i++)
        {
            front[i] = (ushort)(100 + i * 2);
            rear[i] = (ushort)(200 + i * 2);
        }
        for (int i = 50; i < 100; i++)
        {
            front[i] = (ushort)(200 - (i - 50) * 2);
            rear[i] = (ushort)(300 - (i - 50) * 2);
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = rear,
            Markers = new[] { new MarkerData(0.5) },
            ImuData = new RawImuData { SampleRate = 100 }
        };

        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0, 160.0, 150.0,
            (v) => v / 10.0,
            (v) => v / 10.0
        );

        // Act
        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        // Assert
        Assert.Equal(rawData.Markers, result.Markers);
        Assert.Equal(rawData.ImuData, result.ImuData);
        Assert.True(result.Front.Present);
        Assert.True(result.Rear.Present);
    }

    [Fact]
    public void FromRecording_WithLinearCompressionAndRebound_CalculatesExactVelocityAndStrokes()
    {
        // Arrange
        var count = 200;
        var front = new ushort[count];
        var rear = new ushort[count];

        // Calibration: val / 10.0 = mm.
        // 0 to 100 samples: Compression 0 -> 20mm. Velocity = 20mm / 0.1s = 200 mm/s.
        // 100 to 200 samples: Rebound 20mm -> 0mm. Velocity = -200 mm/s.

        for (int i = 0; i < 100; i++)
        {
            front[i] = (ushort)(i * 2);
            rear[i] = (ushort)(i * 2);
        }
        for (int i = 100; i < 200; i++)
        {
            front[i] = (ushort)(200 - (i - 100) * 2);
            rear[i] = (ushort)(200 - (i - 100) * 2);
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = rear
        };

        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0, 100.0, 100.0,
            (v) => v / 10.0,
            (v) => v / 10.0
        );

        // Act
        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        // Assert
        Assert.True(result.Front.Present);

        // Floats being floats
        Assert.Equal(200, result.Front.Velocity[50], 0.0001);
        Assert.Equal(-200, result.Front.Velocity[150], 0.0001);

        var compression = Assert.Single(result.Front.Strokes.Compressions);
        var rebound = Assert.Single(result.Front.Strokes.Rebounds);

        Assert.Equal(0, compression.Start);
        Assert.Equal(99, compression.End);
        Assert.Equal(100, rebound.Start);
        Assert.Equal(199, rebound.End);
    }

    [Fact]
    public void FromRecording_WithIdlingAndLanding_DetectsAirtime()
    {
        // Arrange
        var count = 600;
        var front = new ushort[count];
        var rear = new ushort[count];

        // 0-100: Rebound 20mm -> 0mm (Velocity -200 mm/s)
        for (int i = 0; i < 100; i++)
        {
            front[i] = (ushort)(200 - i * 2);
            rear[i] = (ushort)(200 - i * 2);
        }
        // 100-400: Airtime (Idling at 0mm for 300ms)
        for (int i = 100; i < 400; i++)
        {
            front[i] = 0;
            rear[i] = 0;
        }
        // 400-500: Landing (Compression 0 -> 60mm. Velocity 600 mm/s)
        for (int i = 400; i < 500; i++)
        {
            front[i] = (ushort)((i - 400) * 6);
            rear[i] = (ushort)((i - 400) * 6);
        }
        // 500-600: Rebound
        for (int i = 500; i < 600; i++)
        {
            front[i] = (ushort)(600 - (i - 500) * 6);
            rear[i] = (ushort)(600 - (i - 500) * 6);
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = rear
        };

        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0, 100.0, 100.0,
            (v) => v / 10.0,
            (v) => v / 10.0
        );

        // Act
        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        // Assert
        Assert.NotEmpty(result.Airtimes);
    }

    [Fact]
    public void FromRecording_WithOffsetAirtimeCandidates_UsesOverlapStart()
    {
        var count = 700;
        var front = new ushort[count];
        var rear = new ushort[count];

        for (int i = 0; i < 100; i++)
        {
            front[i] = (ushort)(200 - i * 2);
        }
        for (int i = 100; i < 400; i++)
        {
            front[i] = 0;
        }
        for (int i = 400; i < 500; i++)
        {
            front[i] = (ushort)((i - 400) * 6);
        }
        for (int i = 500; i < 600; i++)
        {
            front[i] = (ushort)(600 - (i - 500) * 6);
        }

        for (int i = 0; i < 150; i++)
        {
            rear[i] = (ushort)(300 - i * 2);
        }
        for (int i = 150; i < 450; i++)
        {
            rear[i] = 0;
        }
        for (int i = 450; i < 550; i++)
        {
            rear[i] = (ushort)((i - 450) * 6);
        }
        for (int i = 550; i < 650; i++)
        {
            rear[i] = (ushort)(600 - (i - 550) * 6);
        }

        var combinedRawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = rear
        };

        var frontOnlyRawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = []
        };

        var rearOnlyRawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = [],
            Rear = rear
        };

        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0, 100.0, 100.0,
            value => value / 10.0,
            value => value / 10.0
        );

        var result = TelemetryData.FromRecording(combinedRawData, metadata, bikeData);
        var frontOnlyResult = TelemetryData.FromRecording(frontOnlyRawData, metadata, bikeData);
        var rearOnlyResult = TelemetryData.FromRecording(rearOnlyRawData, metadata, bikeData);

        var airtime = Assert.Single(result.Airtimes);
        var frontOnlyAirtime = Assert.Single(frontOnlyResult.Airtimes);
        var rearOnlyAirtime = Assert.Single(rearOnlyResult.Airtimes);

        Assert.Equal(Math.Max(frontOnlyAirtime.Start, rearOnlyAirtime.Start), airtime.Start, 3);
        Assert.Equal(Math.Min(frontOnlyAirtime.End, rearOnlyAirtime.End), airtime.End, 3);
    }

    [Fact]
    public void BinarySerialization_WithV4Data_PreservesMarkersAndImuData()
    {
        // Arrange
        var td = new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000 },
            Front = new Suspension { Present = true, Travel = [1.0], Velocity = [0.1], Strokes = new Strokes() },
            Rear = new Suspension { Present = true, Travel = [1.0], Velocity = [0.1], Strokes = new Strokes() },
            Airtimes = [],
            Markers = [new MarkerData(1.23)],
            ImuData = new RawImuData
            {
                SampleRate = 100,
                Meta = [new ImuMetaEntry(0, 16384.0f, 16.4f)],
                Records = [new ImuRecord(1, 2, 3, 4, 5, 6)]
            }
        };

        // Act
        var bytes = td.BinaryForm;
        var result = TelemetryData.FromBinary(bytes);

        // Assert
        Assert.NotNull(result.Markers);
        Assert.Single(result.Markers);
        Assert.Equal(1.23, result.Markers[0].TimestampOffset);
        Assert.NotNull(result.ImuData);
        Assert.Equal(100, result.ImuData.SampleRate);
        Assert.Single(result.ImuData.Meta);
        Assert.Single(result.ImuData.Records);
        Assert.Equal(3, result.ImuData.Records[0].Az);
    }

    [Fact]
    public void FromRecording_WithStationarySignals_HasNoBalanceData_AndZeroVelocityBands()
    {
        var front = Enumerable.Repeat((ushort)1000, 200).ToArray();
        var rear = Enumerable.Repeat((ushort)1000, 200).ToArray();

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = rear,
        };

        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0,
            100.0,
            100.0,
            value => value / 10.0,
            value => value / 10.0);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);
        var frontBands = result.CalculateVelocityBands(SuspensionType.Front, 200.0);

        Assert.False(result.HasBalanceData(BalanceType.Compression));
        Assert.False(result.HasBalanceData(BalanceType.Rebound));
        Assert.Equal(0, frontBands.LowSpeedCompression);
        Assert.Equal(0, frontBands.HighSpeedCompression);
        Assert.Equal(0, frontBands.LowSpeedRebound);
        Assert.Equal(0, frontBands.HighSpeedRebound);
    }

    [Fact]
    public void HistogramAndStatistics_WithPresentSuspensionButNoStrokes_ReturnSafeDefaults()
    {
        var telemetry = new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000 },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 200,
                Travel = [0, 0, 0],
                Velocity = [0, 0, 0],
                TravelBins = [0, 10, 20],
                VelocityBins = [-100, 0, 100],
                FineVelocityBins = [-100, 0, 100],
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
            },
            Rear = new Suspension
            {
                Present = true,
                MaxTravel = 200,
                Travel = [0, 0, 0],
                Velocity = [0, 0, 0],
                TravelBins = [0, 10, 20],
                VelocityBins = [-100, 0, 100],
                FineVelocityBins = [-100, 0, 100],
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
            },
            Airtimes = [],
        };

        var travelHistogram = telemetry.CalculateTravelHistogram(SuspensionType.Front);
        var velocityHistogram = telemetry.CalculateVelocityHistogram(SuspensionType.Front);
        var normal = telemetry.CalculateNormalDistribution(SuspensionType.Front);
        var travelStatistics = telemetry.CalculateTravelStatistics(SuspensionType.Front);
        var velocityStatistics = telemetry.CalculateVelocityStatistics(SuspensionType.Front);

        Assert.False(telemetry.HasStrokeData(SuspensionType.Front));
        Assert.All(travelHistogram.Values, value => Assert.Equal(0, value));
        Assert.All(velocityHistogram.Values.SelectMany(values => values), value => Assert.Equal(0, value));
        Assert.Empty(normal.Y);
        Assert.Empty(normal.Pdf);
        Assert.Equal(0, travelStatistics.Max);
        Assert.Equal(0, travelStatistics.Average);
        Assert.Equal(0, velocityStatistics.AverageCompression);
        Assert.Equal(0, velocityStatistics.AverageRebound);
    }
}
