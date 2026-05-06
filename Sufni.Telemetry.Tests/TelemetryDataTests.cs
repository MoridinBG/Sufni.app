using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class TelemetryDataTests
{
    [Fact]
    public void FromRecording_WithLinearHighAdcSamples_DoesNotWrapMeasurements()
    {
        var samples = new ushort[80];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (ushort)(2500 + index % 5);
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = samples,
            Rear = []
        };
        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0,
            4095.0,
            null,
            measurement => measurement,
            null);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        Assert.All(result.Front.Travel.Take(samples.Length), value => Assert.InRange(value, 2500.0, 2504.0));
    }

    [Fact]
    public void FromRecording_WithRotationalWrapEdgeSamples_DoesNotShiftRecoveredTail()
    {
        var samples = new ushort[240];
        Array.Fill(samples, (ushort)60);
        for (var index = 120; index < 125; index++)
        {
            samples[index] = 4095;
        }

        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = samples,
            Rear = []
        };
        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0,
            4095.0,
            null,
            measurement => measurement,
            null,
            FrontMeasurementWraps: true);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        Assert.All(result.Front.Travel.Skip(130), value => Assert.Equal(60.0, value));
    }

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
            },
            TemperatureAverages =
            [
                new TemperatureAverage(0, 22.5),
                new TemperatureAverage(2, 27.25)
            ]
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
        Assert.Equal(2, result.TemperatureAverages.Length);
        Assert.Equal(0, result.TemperatureAverages[0].LocationId);
        Assert.Equal(22.5, result.TemperatureAverages[0].TemperatureCelsius);
        Assert.Equal(2, result.TemperatureAverages[1].LocationId);
        Assert.Equal(27.25, result.TemperatureAverages[1].TemperatureCelsius);
    }

    [Fact]
    public void FromRecording_ComputesTemperatureAveragesByLocation()
    {
        var samples = Enumerable.Range(0, 200).Select(value => (ushort)value).ToArray();
        var rawData = new RawTelemetryData
        {
            Version = 4,
            SampleRate = 1000,
            Front = samples,
            Rear = samples,
            TemperatureData =
            [
                new TemperatureSample(123, 1, 20.0f),
                new TemperatureSample(124, 1, 24.0f),
                new TemperatureSample(125, 0, 18.5f),
                new TemperatureSample(126, 0, 19.5f)
            ]
        };
        var metadata = new Metadata { SampleRate = 1000 };
        var bikeData = new BikeData(
            65.0,
            100.0,
            100.0,
            measurement => measurement,
            measurement => measurement);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        Assert.Equal(2, result.TemperatureAverages.Length);
        Assert.Equal(0, result.TemperatureAverages[0].LocationId);
        Assert.Equal(19.0, result.TemperatureAverages[0].TemperatureCelsius);
        Assert.Equal(1, result.TemperatureAverages[1].LocationId);
        Assert.Equal(22.0, result.TemperatureAverages[1].TemperatureCelsius);
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
        var frontBands = TelemetryStatistics.CalculateVelocityBands(result, SuspensionType.Front, 200.0);

        Assert.False(TelemetryStatistics.HasBalanceData(result, BalanceType.Compression));
        Assert.False(TelemetryStatistics.HasBalanceData(result, BalanceType.Rebound));
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

        var travelHistogram = TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front);
        var velocityHistogram = TelemetryStatistics.CalculateVelocityHistogram(telemetry, SuspensionType.Front);
        var normal = TelemetryStatistics.CalculateNormalDistribution(telemetry, SuspensionType.Front);
        var travelStatistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front);
        var velocityStatistics = TelemetryStatistics.CalculateVelocityStatistics(telemetry, SuspensionType.Front);

        Assert.False(TelemetryStatistics.HasStrokeData(telemetry, SuspensionType.Front));
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

        var frontHistogram = TelemetryStatistics.CalculateTravelHistogram(result, SuspensionType.Front);
        var rearHistogram = TelemetryStatistics.CalculateTravelHistogram(result, SuspensionType.Rear);
        var frontVelocityHistogram = TelemetryStatistics.CalculateVelocityHistogram(result, SuspensionType.Front);

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

        var histogram = TelemetryStatistics.CalculateStrokeLengthHistogram(telemetry, SuspensionType.Front, BalanceType.Compression);

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

        var histogram = TelemetryStatistics.CalculateStrokeSpeedHistogram(telemetry, SuspensionType.Front, BalanceType.Compression);

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

        var histogram = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);

        Assert.Equal(6, histogram.Bins.Count);
        Assert.Equal(5, histogram.Values.Count);
        Assert.Equal(2, histogram.Values.Sum());
        Assert.DoesNotContain(100.0, histogram.Bins);
    }

    [Fact]
    public void CalculateTravelStatistics_WithRange_UsesOnlyWholeIncludedStrokes()
    {
        var telemetry = CreateTelemetry(
            travel: Enumerable.Range(0, 10).Select(index => (double)index).ToArray(),
            maxTravel: 100,
            sampleRate: 10,
            compressions:
            [
                CreateStroke(0, 1, maxTravel: 10),
                CreateStroke(3, 4, maxTravel: 40),
                CreateStroke(7, 8, maxTravel: 80),
            ]);
        var range = new TelemetryTimeRange(0.1, 0.65);

        var fullStatistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front);
        var rangeStatistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front, range);

        Assert.True(TelemetryStatistics.HasStrokeData(telemetry, SuspensionType.Front, range));
        Assert.Equal(80, fullStatistics.Max);
        Assert.Equal(40, rangeStatistics.Max);
    }

    [Fact]
    public void CalculateTravelStatistics_WithRangeExcludingWholeStrokes_ReturnsSafeDefaults()
    {
        var telemetry = CreateTelemetry(
            travel: Enumerable.Range(0, 10).Select(index => (double)index).ToArray(),
            maxTravel: 100,
            sampleRate: 10,
            compressions:
            [
                CreateStroke(0, 1, maxTravel: 10),
                CreateStroke(3, 4, maxTravel: 40),
            ]);
        var range = new TelemetryTimeRange(0.15, 0.35);

        var rangeStatistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front, range);

        Assert.False(TelemetryStatistics.HasStrokeData(telemetry, SuspensionType.Front, range));
        Assert.Equal(0, rangeStatistics.Max);
        Assert.Equal(0, rangeStatistics.Average);
        Assert.Equal(0, rangeStatistics.Bottomouts);
    }

    [Fact]
    public void CalculateTravelStatistics_WithDynamicSag_UsesSelectedTravelSamples()
    {
        var travel = new[] { 0.0, 38.0, 39.0, 0.0, 38.5 };
        var telemetry = CreateTelemetry(travel, maxTravel: 40, sampleRate: 10);
        var options = new TravelStatisticsOptions(HistogramMode: TravelHistogramMode.DynamicSag);

        var histogram = TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front, options);
        var statistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front, options);

        Assert.Equal(100, histogram.Values.Sum(), 3);
        Assert.Equal(39, statistics.Max);
        Assert.Equal(travel.Average(), statistics.Average, 6);
        Assert.Equal(2, statistics.Bottomouts);
    }

    [Fact]
    public void CalculateBalance_WithTravelMode_UsesStrokeLengthInsteadOfZenith()
    {
        var telemetry = CreateBalanceTelemetry(
            frontTravel: [0, 20, 0, 60],
            rearTravel: [0, 30, 0, 80],
            frontCompressions:
            [
                CreateStroke(0, 1, maxVelocity: 100, maxTravel: 50),
                CreateStroke(2, 3, maxVelocity: 200, maxTravel: 70),
            ],
            rearCompressions:
            [
                CreateStroke(0, 1, maxVelocity: 100, maxTravel: 40),
                CreateStroke(2, 3, maxVelocity: 200, maxTravel: 90),
            ]);

        var zenith = TelemetryStatistics.CalculateBalance(telemetry, BalanceType.Compression);
        var travel = TelemetryStatistics.CalculateBalance(telemetry,
            BalanceType.Compression,
            new BalanceStatisticsOptions(DisplacementMode: BalanceDisplacementMode.Travel));

        Assert.Equal([50, 70], zenith.FrontTravel);
        Assert.Equal([40, 90], zenith.RearTravel);
        Assert.Equal([20, 60], travel.FrontTravel);
        Assert.Equal([30, 80], travel.RearTravel);
    }

    [Fact]
    public void CalculateBalance_ReturnsSlopeDeltaPercent()
    {
        var telemetry = CreateBalanceTelemetry(
            frontTravel: [0, 10, 0, 20],
            rearTravel: [0, 10, 0, 20],
            frontCompressions:
            [
                CreateStroke(0, 1, maxVelocity: 100, maxTravel: 10),
                CreateStroke(2, 3, maxVelocity: 200, maxTravel: 20),
            ],
            rearCompressions:
            [
                CreateStroke(0, 1, maxVelocity: 100, maxTravel: 10),
                CreateStroke(2, 3, maxVelocity: 150, maxTravel: 20),
            ]);

        var balance = TelemetryStatistics.CalculateBalance(telemetry, BalanceType.Compression);

        Assert.Equal(10, balance.FrontSlope, 6);
        Assert.Equal(5, balance.RearSlope, 6);
        Assert.Equal(50, balance.SignedSlopeDeltaPercent, 6);
        Assert.Equal(50, balance.AbsoluteSlopeDeltaPercent, 6);
    }

    [Fact]
    public void CalculateVelocityStatistics_WithStrokePeakAverageAndPercentile_UsesStrokePeaks()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20, 10, 0],
            maxTravel: 100,
            compressions:
            [
                CreateStroke(0, 2, maxVelocity: 100, sumVelocity: 300),
                CreateStroke(3, 4, maxVelocity: 500, sumVelocity: 1000),
            ],
            rebounds:
            [
                CreateStroke(0, 2, maxVelocity: -200, sumVelocity: -600),
                CreateStroke(3, 4, maxVelocity: -800, sumVelocity: -1600),
            ]);

        var sampleAverage = TelemetryStatistics.CalculateVelocityStatistics(telemetry, SuspensionType.Front);
        var strokePeakAverage = TelemetryStatistics.CalculateVelocityStatistics(telemetry,
            SuspensionType.Front,
            new VelocityStatisticsOptions(VelocityAverageMode: VelocityAverageMode.StrokePeakAveraged));

        Assert.Equal(260, sampleAverage.AverageCompression);
        Assert.Equal(-440, sampleAverage.AverageRebound);
        Assert.Equal(300, strokePeakAverage.AverageCompression);
        Assert.Equal(-500, strokePeakAverage.AverageRebound);
        Assert.Equal(500, strokePeakAverage.Percentile95Compression);
        Assert.Equal(-800, strokePeakAverage.Percentile95Rebound);
        Assert.Equal(2, strokePeakAverage.CompressionStrokeCount);
        Assert.Equal(2, strokePeakAverage.ReboundStrokeCount);
    }

    [Fact]
    public void CalculateVelocityHistogram_WithStrokePeakAverage_UsesOnePeakPerStroke()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20],
            maxTravel: 100,
            compressions:
            [
                CreateStroke(
                    0,
                    1,
                    maxVelocity: 50,
                    maxTravel: 20,
                    digitizedTravel: [1, 1],
                    digitizedVelocity: [1, 1]),
            ],
            rebounds:
            [
                CreateStroke(
                    2,
                    2,
                    maxVelocity: -50,
                    maxTravel: 80,
                    digitizedTravel: [15],
                    digitizedVelocity: [0]),
            ]);

        var sampleHistogram = TelemetryStatistics.CalculateVelocityHistogram(telemetry, SuspensionType.Front);
        var strokePeakHistogram = TelemetryStatistics.CalculateVelocityHistogram(telemetry,
            SuspensionType.Front,
            new VelocityStatisticsOptions(VelocityAverageMode: VelocityAverageMode.StrokePeakAveraged));

        Assert.Equal(100, sampleHistogram.Values.SelectMany(values => values).Sum(), 3);
        Assert.Equal(100, strokePeakHistogram.Values.SelectMany(values => values).Sum(), 3);
        Assert.True(sampleHistogram.Values[1][0] > 0);
        Assert.Equal(0, strokePeakHistogram.Values[1][0]);
        Assert.Equal(50, strokePeakHistogram.Values[1][1], 3);
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

        var stats = TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front);

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

        var stats = TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(2, stats.MagicCarpet, 3);
    }

    [Fact]
    public void CalculateVibration_MagicCarpetDoesNotInflateWithImuOversampling()
    {
        // Both recordings cover the same 300 s of physical IMU data; the oversampled one
        // just samples 5x more often, so sampleCount scales with sampleRate.
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
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 5, sampleCount: 1500, vibrationG: 1));

        var baseStats = TelemetryStatistics.CalculateVibration(baseTelemetry, ImuLocation.Fork, SuspensionType.Front);
        var oversampledStats = TelemetryStatistics.CalculateVibration(oversampledTelemetry, ImuLocation.Fork, SuspensionType.Front);

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

        var stats = TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front);

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

        Assert.False(TelemetryStatistics.HasVibrationData(telemetry, ImuLocation.Fork));
        Assert.Null(TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front));
    }

    [Fact]
    public void CalculateVibration_ForInactiveLocation_ReturnsNull()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        Assert.False(TelemetryStatistics.HasVibrationData(telemetry, ImuLocation.Frame));
        Assert.Null(TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Frame, SuspensionType.Front));
    }

    [Fact]
    public void CalculateVibration_WithoutPairedStrokeData_ReturnsNull()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10],
            maxTravel: 100,
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        Assert.Null(TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front));
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

        Assert.Null(TelemetryStatistics.CalculateVibration(invalidScaleTelemetry, ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(TelemetryStatistics.CalculateVibration(missingMetaTelemetry, ImuLocation.Fork, SuspensionType.Front));
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

        Assert.Null(TelemetryStatistics.CalculateVibration(missingTravelTelemetry, ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(TelemetryStatistics.CalculateVibration(zeroMaxTravelTelemetry, ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(TelemetryStatistics.CalculateVibration(zeroTelemetrySampleRate, ImuLocation.Fork, SuspensionType.Front));
        Assert.Null(TelemetryStatistics.CalculateVibration(zeroImuSampleRate, ImuLocation.Fork, SuspensionType.Front));
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

    private static TelemetryData CreateBalanceTelemetry(
        double[] frontTravel,
        double[] rearTravel,
        Stroke[] frontCompressions,
        Stroke[] rearCompressions,
        double maxTravel = 100)
    {
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = frontTravel.Length / 1000.0 },
            Front = CreateSuspension(frontTravel, maxTravel, frontCompressions, []),
            Rear = CreateSuspension(rearTravel, maxTravel, rearCompressions, []),
            Airtimes = [],
            Markers = [],
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

    private static Stroke CreateStroke(
        int start,
        int end,
        double maxVelocity = 0,
        double? maxTravel = null,
        double sumVelocity = 0,
        int[]? digitizedTravel = null,
        int[]? digitizedVelocity = null)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                SumVelocity = sumVelocity,
                MaxVelocity = maxVelocity,
                MaxTravel = maxTravel ?? 0,
                Count = end - start + 1,
            },
            DigitizedTravel = digitizedTravel ?? [],
            DigitizedVelocity = digitizedVelocity ?? [],
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
