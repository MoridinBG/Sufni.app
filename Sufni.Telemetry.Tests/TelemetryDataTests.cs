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

    [Fact]
    public void FromRecording_WithBottomingOutAndIrrationalMaxTravel_ProducesValidHistograms()
    {
        // MaxTravel built like a real setup (ForkStroke * sin(headAngle)) is unlikely
        // to divide cleanly by TravelHistBins, so Linspace can produce a last bin edge
        // that differs from MaxTravel by 1-2 ULPs. Combined with Math.Clamp emitting
        // exactly MaxTravel on bottom-outs, this caused Digitize to return an index
        // one past the histogram array. 140 mm at 65° is a configuration where the
        // computed bins[^1] sits 1 ULP below MaxTravel.
        var maxTravel = 140.0 * Math.Sin(65.0 * Math.PI / 180.0);

        var count = 200;
        var front = new ushort[count];
        var rear = new ushort[count];

        // Compression that drives the suspension well past MaxTravel so Math.Clamp
        // produces samples equal to MaxTravel.
        for (int i = 0; i < 100; i++)
        {
            front[i] = (ushort)(i * 30);
            rear[i] = (ushort)(i * 30);
        }
        for (int i = 100; i < 200; i++)
        {
            front[i] = (ushort)((200 - i) * 30);
            rear[i] = (ushort)((200 - i) * 30);
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = front,
            Rear = rear,
        };

        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0, maxTravel, maxTravel,
            v => v / 10.0,
            v => v / 10.0);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        var frontHistogram = result.CalculateTravelHistogram(SuspensionType.Front);
        var rearHistogram = result.CalculateTravelHistogram(SuspensionType.Rear);
        var frontVelocityHistogram = result.CalculateVelocityHistogram(SuspensionType.Front);

        Assert.Equal(Parameters.TravelHistBins, frontHistogram.Values.Count);
        Assert.Equal(Parameters.TravelHistBins, rearHistogram.Values.Count);
        Assert.NotEmpty(frontVelocityHistogram.Values);
    }

    [Fact]
    public void CalculateStrokeLengthHistogram_BucketsStrokeLengthsAsPercentages()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 25, 0, 75, 0, 150],
            maxTravel: 200,
            compressions:
            [
                CreateStroke(0, 1),
                CreateStroke(2, 3),
                CreateStroke(4, 5),
            ]);

        var histogram = telemetry.CalculateStrokeLengthHistogram(SuspensionType.Front, BalanceType.Compression);

        Assert.Equal(Parameters.TravelHistBins + 1, histogram.Bins.Count);
        Assert.Equal(100.0 / 3.0, histogram.Values[2], 3);
        Assert.Equal(100.0 / 3.0, histogram.Values[7], 3);
        Assert.Equal(100.0 / 3.0, histogram.Values[14], 3);
        Assert.Equal(100, histogram.Values.Sum(), 3);
    }

    [Fact]
    public void CalculateStrokeSpeedHistogram_BucketsPeakStrokeSpeedsAsPercentages()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20],
            maxTravel: 200,
            compressions:
            [
                CreateStroke(0, 0, maxVelocity: 250),
                CreateStroke(1, 1, maxVelocity: 1500),
                CreateStroke(2, 2, maxVelocity: 3500),
            ]);

        var histogram = telemetry.CalculateStrokeSpeedHistogram(SuspensionType.Front, BalanceType.Compression);

        Assert.Equal(36, histogram.Bins.Count);
        Assert.Equal(100.0 / 3.0, histogram.Values[2], 3);
        Assert.Equal(100.0 / 3.0, histogram.Values[14], 3);
        Assert.Equal(100.0 / 3.0, histogram.Values[34], 3);
        Assert.Equal(100, histogram.Values.Sum(), 3);
    }

    [Fact]
    public void CalculateDeepTravelHistogram_CountsCompressionStrokesEnteringTopQuarter()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 100, 160, 190],
            maxTravel: 200,
            compressions:
            [
                CreateStroke(0, 0, maxTravel: 100),
                CreateStroke(1, 1, maxTravel: 160),
                CreateStroke(2, 2, maxTravel: 190),
            ]);

        var histogram = telemetry.CalculateDeepTravelHistogram(SuspensionType.Front);

        Assert.Equal(6, histogram.Bins.Count);
        Assert.Equal(5, histogram.Values.Count);
        Assert.Equal(2, histogram.Values.Sum());
        Assert.DoesNotContain(100.0, histogram.Bins);
    }

    [Fact]
    public void CalculateVibration_SplitsVibrationByCompressionReboundAndOther()
    {
        var travel = Enumerable.Repeat(10.0, 400).ToArray();
        var telemetry = CreateTelemetry(
            travel,
            maxTravel: 100,
            sampleRate: 200,
            compressions: [CreateStroke(0, 99)],
            rebounds: [CreateStroke(100, 199)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 1000, sampleCount: 2000, vibrationG: 1));

        var stats = telemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(25, stats.CompressionPercent, 3);
        Assert.Equal(25, stats.ReboundPercent, 3);
        Assert.Equal(50, stats.OtherPercent, 3);
    }

    [Fact]
    public void CalculateVibration_ComputesMagicCarpetFromSuspensionMovementAndTotalVibration()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 100, 0, 100, 0, 100, 0],
            maxTravel: 100,
            sampleRate: 1,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 1, sampleCount: 300, vibrationG: 1));

        var stats = telemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(2, stats.MagicCarpet, 3);
    }

    [Fact]
    public void CalculateVibration_MagicCarpetDoesNotInflateWithImuOversampling()
    {
        var baseTelemetry = CreateTelemetry(
            travel: [0, 100, 0, 100, 0, 100, 0],
            maxTravel: 100,
            sampleRate: 1,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 1, sampleCount: 300, vibrationG: 1));
        var oversampledTelemetry = CreateTelemetry(
            travel: [0, 100, 0, 100, 0, 100, 0],
            maxTravel: 100,
            sampleRate: 1,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 5, sampleCount: 300, vibrationG: 1));

        var baseStats = baseTelemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front);
        var oversampledStats = oversampledTelemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(baseStats);
        Assert.NotNull(oversampledStats);
        Assert.Equal(baseStats.MagicCarpet, oversampledStats.MagicCarpet, 6);
    }

    [Fact]
    public void CalculateVibration_SplitsCompressionVibrationByTravelThirds()
    {
        var telemetry = CreateTelemetry(
            travel: [10, 50, 90],
            maxTravel: 100,
            sampleRate: 3,
            compressions: [CreateStroke(0, 2)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 3, sampleCount: 3, vibrationG: 1));

        var stats = telemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(100.0 / 3.0, stats.CompressionThirds.Lower, 3);
        Assert.Equal(100.0 / 3.0, stats.CompressionThirds.Middle, 3);
        Assert.Equal(100.0 / 3.0, stats.CompressionThirds.Upper, 3);
    }

    [Fact]
    public void CalculateVibration_WithoutImuData_ReturnsNull()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)]);

        Assert.False(telemetry.HasVibrationData(ImuLocation.Fork));
        Assert.Null(telemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
    }

    [Fact]
    public void CalculateVibration_ForInactiveLocation_ReturnsNull()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        Assert.False(telemetry.HasVibrationData(ImuLocation.Frame));
        Assert.Null(telemetry.CalculateVibration(ImuLocation.Frame, SuspensionType.Front));
    }

    [Fact]
    public void CalculateVibration_WithoutPairedStrokeData_ReturnsNull()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        Assert.Null(telemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
    }

    [Fact]
    public void CalculateVibration_WithInvalidScaleOrMissingMeta_ReturnsNull()
    {
        var invalidScaleTelemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));
        invalidScaleTelemetry.ImuData!.Meta[0] = new ImuMetaEntry((byte)ImuLocation.Fork, 0, 1.0f);

        var missingMetaTelemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));
        missingMetaTelemetry.ImuData!.Meta.Clear();

        Assert.Null(invalidScaleTelemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(missingMetaTelemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
    }

    [Fact]
    public void CalculateVibration_WithInvalidTravelOrSampleRates_ReturnsNull()
    {
        var missingTravelTelemetry = CreateTelemetry(
            travel: [10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 0)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        var zeroMaxTravelTelemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 0,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        var zeroTelemetrySampleRate = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));
        zeroTelemetrySampleRate.Metadata.SampleRate = 0;

        var zeroImuSampleRate = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));
        zeroImuSampleRate.ImuData!.SampleRate = 0;

        Assert.Null(missingTravelTelemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(zeroMaxTravelTelemetry.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(zeroTelemetrySampleRate.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(zeroImuSampleRate.CalculateVibration(ImuLocation.Fork, SuspensionType.Front));
    }

    private static TelemetryData CreateTelemetry(
        double[] travel,
        double maxTravel,
        int sampleRate = 1000,
        Stroke[]? compressions = null,
        Stroke[]? rebounds = null,
        RawImuData? imuData = null)
    {
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = sampleRate, Duration = travel.Length / (double)sampleRate },
            Front = CreateSuspension(travel, maxTravel, compressions, rebounds),
            Rear = CreateSuspension(travel, maxTravel, [], []),
            Airtimes = [],
            Markers = [],
            ImuData = imuData,
        };
    }

    private static Suspension CreateSuspension(
        double[] travel,
        double maxTravel,
        Stroke[]? compressions,
        Stroke[]? rebounds)
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = maxTravel,
            Travel = travel,
            Velocity = new double[travel.Length],
            TravelBins = CreateTravelBins(maxTravel),
            VelocityBins = [-100, 0, 100],
            FineVelocityBins = [-100, 0, 100],
            Strokes = new Strokes
            {
                Compressions = compressions ?? [],
                Rebounds = rebounds ?? [],
            },
        };
    }

    private static double[] CreateTravelBins(double maxTravel)
    {
        return Enumerable.Range(0, Parameters.TravelHistBins + 1)
            .Select(index => maxTravel / Parameters.TravelHistBins * index)
            .ToArray();
    }

    private static Stroke CreateStroke(int start, int end, double maxVelocity = 0, double? maxTravel = null)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                MaxVelocity = maxVelocity,
                MaxTravel = maxTravel ?? 0,
                Count = end - start + 1,
            },
            DigitizedTravel = [],
            DigitizedVelocity = [],
            FineDigitizedVelocity = [],
        };
    }

    private static RawImuData CreateImuData(ImuLocation location, int sampleRate, int sampleCount, double vibrationG)
    {
        const float accelScale = 1000.0f;
        var az = (short)Math.Round((1.0 + vibrationG) * accelScale);
        return new RawImuData
        {
            SampleRate = sampleRate,
            ActiveLocations = [(byte)location],
            Meta = [new ImuMetaEntry((byte)location, accelScale, 1.0f)],
            Records = Enumerable.Range(0, sampleCount)
                .Select(_ => new ImuRecord(0, 0, az, 0, 0, 0))
                .ToList(),
        };
    }
}
