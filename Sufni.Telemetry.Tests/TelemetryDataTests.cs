using MessagePack;
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class TelemetryDataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FromRecording_WithoutCalibration_ProcessesOnlyTheCalibratedSide(bool segmented, bool missingFront)
    {
        var samples = Enumerable.Range(1, 20).Select(value => (ushort)value).ToArray();
        var raw = new RawTelemetryData { Version = 4, SampleRate = 100, Front = samples, Rear = samples };
        if (segmented)
        {
            raw.Version = 5;
            raw.FrontSegments = [new() { Counts = samples[..10] }, new() { FirstIndex = 10, FirstMonotonicDeltaUs = 100_000, Counts = samples[10..] }];
            raw.RearSegments = raw.FrontSegments;
        }
        var bike = missingFront
            ? new BikeData(null, 100, null, value => value)
            : new BikeData(100, null, value => value, null);

        var result = TelemetryData.FromRecording(raw, new Metadata { SampleRate = 100 }, bike);

        Assert.Equal(!missingFront, result.Front.Present);
        Assert.Equal(missingFront, result.Rear.Present);
        Assert.Equal(20, (missingFront ? result.Rear : result.Front).Travel.Length);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData(ushort.MaxValue)]
    public void FromRecording_V4SentinelRearChannel_ProducesPersistedFrontOnlySession(ushort sentinel)
    {
        using var stream = SstTestFiles.CreateV4Stream(123,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 100)),
            SstTestFiles.Telemetry(Enumerable.Range(1, 20).Select(value => ((ushort)value, sentinel)).ToArray()));
        var raw = RawTelemetryData.FromStream(stream);

        var result = TelemetryData.FromRecording(raw, new Metadata { SampleRate = 100 },
            new BikeData(100, 100, value => value, value => value));

        Assert.True(result.Front.Present);
        Assert.False(result.Rear.Present);
        Assert.Equal(20, result.Front.Travel.Length);
        var restored = TelemetryData.FromBinary(result.BinaryForm);
        Assert.True(restored.Front.Present);
        Assert.False(restored.Rear.Present);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData(ushort.MaxValue)]
    public void FromRecording_SegmentedSentinelFrontChannel_PreservesRearTravel(ushort sentinel)
    {
        var raw = CreateSegmentedRaw(
            new() { Counts = Enumerable.Repeat(sentinel, 10).ToArray() },
            new() { FirstIndex = 20, FirstMonotonicDeltaUs = 200_000, Counts = Enumerable.Repeat(sentinel, 10).ToArray() });
        raw.RearSegments = [new() { Counts = Enumerable.Range(1, 30).Select(value => (ushort)value).ToArray() }];

        var result = TelemetryData.FromRecording(raw, new Metadata { SampleRate = 100 },
            new BikeData(100, 100, value => value, value => value));

        Assert.False(result.Front.Present);
        Assert.True(result.Rear.Present);
        Assert.Equal(30, result.Rear.Travel.Length);
    }

    [Theory]
    [InlineData((ushort)0, (ushort)100)]
    [InlineData(ushort.MaxValue, (ushort)100)]
    [InlineData((ushort)0, ushort.MaxValue)]
    public void FromRecording_MixedSamples_KeepChannelPresent(ushort first, ushort second)
    {
        var samples = Enumerable.Repeat(first, 10).Concat(Enumerable.Repeat(second, 10)).ToArray();
        var raw = new RawTelemetryData { Version = 4, Front = samples };

        var result = TelemetryData.FromRecording(raw, new Metadata { SampleRate = 100 },
            new BikeData(4095, null, value => value, null));

        Assert.True(result.Front.Present);
        Assert.Equal(samples.Length, result.Front.Travel.Length);
    }

    [Fact]
    public void FromRecording_NoUsableChannel_ThrowsTypedFailure()
    {
        var raw = new RawTelemetryData { Front = new ushort[20], Rear = Enumerable.Repeat(ushort.MaxValue, 20).ToArray() };

        var failure = Assert.Throws<TelemetryProcessingException>(() => TelemetryData.FromRecording(raw,
            new Metadata { SampleRate = 100 }, new BikeData(100, 100, value => value, value => value)));

        Assert.Equal(TelemetryProcessingFailureReason.NoTravelData, failure.Reason);
    }

    [Fact]
    public void FromRecording_UnequalPresentChannels_ThrowsTypedFailure()
    {
        var raw = new RawTelemetryData { Front = [1, 2, 3, 4, 5], Rear = [1, 2, 3, 4, 5, 6] };

        var failure = Assert.Throws<TelemetryProcessingException>(() => TelemetryData.FromRecording(raw,
            new Metadata { SampleRate = 100 }, new BikeData(100, 100, value => value, value => value)));

        Assert.Equal(TelemetryProcessingFailureReason.RecordCountMismatch, failure.Reason);
    }

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
            4095.0,
            null,
            measurement => measurement,
            null);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        Assert.Equal(samples.Select(value => (double)value), result.Front.Travel);
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
            4095.0,
            null,
            measurement => measurement,
            null,
            FrontMeasurementWraps: true);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);

        Assert.Equal(0, result.Front.AnomalyRate);
        Assert.Equal(samples.Select(value => (double)value), result.Front.Travel);
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
            100.0, 100.0,
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
        // Peak sample 100 now falls in the compression under the v4 SG velocity
        // (TensorPrimitives.Dot/FMA shifts the near-zero peak sign by one sample).
        Assert.Equal(100, compression.End);
        Assert.Equal(101, rebound.Start);
        Assert.Equal(199, rebound.End);
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
            100.0, 100.0,
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
    public void SuspensionTimeSeriesSampler_WithFlatArrays_UsesSegmentDenseOffsets()
    {
        var sampler = new SuspensionTimeSeriesSampler(
            [
                new ProcessedSuspensionSegment { FirstDenseIndex = 0, StartSeconds = 0.0, SampleCount = 2 },
                new ProcessedSuspensionSegment { FirstDenseIndex = 2, StartSeconds = 1.0, SampleCount = 2 },
            ],
            [1.0, 3.0, 10.0, 20.0],
            sampleRate: 10);

        Assert.True(sampler.TrySampleTravel(0.05, out var firstSegmentTravel));
        Assert.Equal(2.0, firstSegmentTravel, precision: 6);
        Assert.True(sampler.TrySampleTravel(1.1, out var secondSegmentTravel));
        Assert.Equal(20.0, secondSegmentTravel, precision: 6);
        Assert.False(sampler.TrySampleTravel(0.5, out _));
    }

    [Fact]
    public void FromBinary_WithLegacyDenseSegment_InferSampleCountFromFlatArrays()
    {
        var bytes = CreateLegacySegmentedTelemetryBlob(
            frontTravel: [1.0, 2.0, 3.0],
            frontVelocity: [10.0, 20.0, 30.0],
            hasGaps: false,
            [
                new LegacyProcessedSuspensionSegment
                {
                    FirstDenseIndex = 0,
                    FirstSourceIndex = 0,
                    StartSeconds = 0.0,
                    Travel = [1.0, 2.0, 3.0],
                    Velocity = [10.0, 20.0, 30.0],
                },
            ]);

        var result = TelemetryData.FromBinary(bytes);

        var segment = Assert.Single(result.Front.Segments);
        Assert.Equal(3, segment.SampleCount);
        Assert.Equal([1.0, 2.0, 3.0], result.Front.Travel);
        Assert.Equal([10.0, 20.0, 30.0], result.Front.Velocity);
    }

    [Fact]
    public void FromBinary_WithLegacyGappedSegments_InferSampleCountsFromDenseOffsets()
    {
        var bytes = CreateLegacySegmentedTelemetryBlob(
            frontTravel: [1.0, 2.0, 10.0, 20.0],
            frontVelocity: [10.0, 20.0, 100.0, 200.0],
            hasGaps: true,
            [
                new LegacyProcessedSuspensionSegment
                {
                    FirstDenseIndex = 0,
                    FirstSourceIndex = 0,
                    StartSeconds = 0.0,
                    Travel = [1.0, 2.0],
                    Velocity = [10.0, 20.0],
                },
                new LegacyProcessedSuspensionSegment
                {
                    FirstDenseIndex = 2,
                    FirstSourceIndex = 10,
                    StartSeconds = 1.0,
                    Travel = [10.0, 20.0],
                    Velocity = [100.0, 200.0],
                },
            ]);

        var result = TelemetryData.FromBinary(bytes);

        Assert.Equal([2, 2], result.Front.Segments.Select(segment => segment.SampleCount));
        Assert.Equal([1.0, 2.0], result.Front.Travel[..2]);
        Assert.Equal([10.0, 20.0], result.Front.Travel[2..]);
        var sampler = new SuspensionTimeSeriesSampler(result.Front.Segments, result.Front.Travel, result.Metadata.SampleRate);
        Assert.True(sampler.TrySampleTravel(1.1, out var travel));
        Assert.Equal(20.0, travel, precision: 6);
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

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void FromRecording_WithTravelGap_ProcessesSegmentsWithoutCrossingGap(byte version)
    {
        var rawData = CreateSegmentedRaw(
            new RawCountSegment
            {
                FirstIndex = 0,
                FirstMonotonicDeltaUs = 0,
                Counts = Enumerable.Range(0, 50).Select(index => (ushort)index).ToArray(),
            },
            new RawCountSegment
            {
                FirstIndex = 100,
                FirstMonotonicDeltaUs = 1_000_000,
                Counts = Enumerable.Range(0, 50).Select(index => (ushort)(50 - index)).ToArray(),
            });

        rawData.Version = version;
        var result = TelemetryData.FromRecording(rawData,
            new Metadata { Version = version, SampleRate = 100, Duration = 1.5 },
            new BikeData(100, null, value => value, null), new TelemetryProcessingOptions(0));

        Assert.True(result.Front.HasGaps);
        Assert.Equal(2, result.Front.Segments.Length);
        Assert.Equal(100, result.Front.Travel.Length);
        Assert.All(result.Front.Velocity.Take(50), value => Assert.Equal(100, value, 9));
        Assert.All(result.Front.Velocity.Skip(50), value => Assert.Equal(-100, value, 9));
        var compression = Assert.Single(result.Front.Strokes.Compressions);
        var rebound = Assert.Single(result.Front.Strokes.Rebounds);
        Assert.Equal((0, 49), (compression.Start, compression.End));
        Assert.Equal((50, 99), (rebound.Start, rebound.End));
        Assert.Equal(0, compression.StartSeconds, 9);
        Assert.Equal(0.49, compression.EndSeconds, 9);
        Assert.Equal(1, rebound.StartSeconds, 9);
        Assert.Equal(1.49, rebound.EndSeconds, 9);
    }

    [Fact]
    public void FromRecording_WithFrontOnlyTravelGap_LeavesRearWithoutGaps()
    {
        var rawData = new RawTelemetryData
        {
            Version = 5,
            SampleRate = 100,
            FrontSegments =
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = Enumerable.Range(0, 50).Select(index => (ushort)index).ToArray(),
                },
                new RawCountSegment
                {
                    FirstIndex = 100,
                    FirstMonotonicDeltaUs = 1_000_000,
                    Counts = Enumerable.Range(0, 50).Select(index => (ushort)(50 - index)).ToArray(),
                },
            ],
            RearSegments =
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = Enumerable.Range(0, 100).Select(index => (ushort)index).ToArray(),
                },
            ],
            StreamGaps =
            [
                new RawStreamGap
                {
                    StreamKind = 1,
                    LocationId = 1, // fork/front travel sensor bit
                    FirstMissingIndex = 50,
                    MissingCount = 50,
                    MissingTimeUs = 500_000,
                    Reason = "index_gap",
                },
            ],
        };

        var result = TelemetryData.FromRecording(
            rawData,
            new Metadata { SampleRate = 100, Duration = 2.0 },
            new BikeData(100, 100, value => value, value => value),
            new TelemetryProcessingOptions(0));

        Assert.True(result.Front.HasGaps);
        Assert.True(result.Rear.Present);
        // The gap is attributed to the front sensor, so the rear side must stay dense.
        Assert.False(result.Rear.HasGaps);
    }

    [Fact]
    public void FromRecording_WithV5ShortSegment_AppendsTravelWithZeroVelocityAndNoStrokes()
    {
        var rawData = CreateSegmentedRaw(new RawCountSegment
        {
            FirstIndex = 10,
            FirstMonotonicDeltaUs = 100_000,
            Counts = [1, 2, 3],
        });

        var result = ProcessV5Segments(0.13, rawData.FrontSegments);

        Assert.True(result.Front.Present);
        Assert.Equal([1.0, 2.0, 3.0], result.Front.Travel);
        Assert.Equal([0.0, 0.0, 0.0], result.Front.Velocity);
        Assert.Empty(result.Front.Strokes.Compressions);
        Assert.Empty(result.Front.Strokes.Rebounds);
    }

    [Fact]
    public void FromRecording_WithStationarySignals_PreservesPresentChannelsWithoutActiveStrokes()
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
            100.0,
            100.0,
            value => value / 10.0,
            value => value / 10.0);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData);
        Assert.True(result.Front.Present);
        Assert.True(result.Rear.Present);
        Assert.All(result.Front.Travel, value => Assert.Equal(100, value));
        Assert.All(result.Rear.Travel, value => Assert.Equal(100, value));
        Assert.Empty(result.Front.Strokes.Compressions);
        Assert.Empty(result.Front.Strokes.Rebounds);
        Assert.Empty(result.Rear.Strokes.Compressions);
        Assert.Empty(result.Rear.Strokes.Rebounds);
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

        // Unit ADC steps avoid spike cleanup; calibration drives travel past MaxTravel so Math.Clamp
        // produces samples equal to MaxTravel.
        for (int i = 0; i < 100; i++)
        {
            front[i] = (ushort)i;
            rear[i] = (ushort)i;
        }
        for (int i = 100; i < 200; i++)
        {
            front[i] = (ushort)(200 - i);
            rear[i] = (ushort)(200 - i);
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
            maxTravel, maxTravel,
            v => v * 3.0,
            v => v * 3.0);

        var result = TelemetryData.FromRecording(rawData, metadata, bikeData, new TelemetryProcessingOptions(0));

        foreach (var side in new[] { SuspensionType.Front, SuspensionType.Rear })
        {
            foreach (var (mode, expectedLastBin) in new[]
            {
                (TravelDistributionMode.ActiveSuspension, 600.0 / 87),
                (TravelDistributionMode.DynamicSag, 59.5),
            })
            {
                var histogram = TelemetryStatistics.CalculateTravelHistogram(result, side,
                    new TravelStatisticsOptions(HistogramMode: mode));
                Assert.Equal(20, histogram.Values.Count);
                Assert.All(histogram.Values, value => Assert.True(double.IsFinite(value)));
                Assert.Equal(100, histogram.Values.Sum(), 9);
                // Six of 87 active samples and 119 of all 200 samples reach the final bin.
                Assert.Equal(expectedLastBin, histogram.Values[^1], 9);
            }
        }
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
        Assert.Equal(100.0 / 3.0, histogram.Values[15], 3);
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
        Assert.Equal(100.0 / 3.0, histogram.Values[15], 3);
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
        double[] travel = [0.0, 38.0, 39.0, 0.0, 38.5];
        var telemetry = CreateTelemetry(travel, maxTravel: 40, sampleRate: 10);
        var options = new TravelStatisticsOptions(HistogramMode: TravelDistributionMode.DynamicSag);

        var statistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front, options);

        Assert.Equal(39, statistics.Max);
        Assert.Equal(23.1, statistics.Average, 6);
        Assert.Equal(2, statistics.Bottomouts);
    }

    [Fact]
    public void CalculateTravelStatistics_WithGappedDynamicSagRange_UsesSegmentTimes()
    {
        var telemetry = CreateTelemetry([1.0, 2.0, 10.0, 20.0], maxTravel: 100, sampleRate: 10);
        telemetry = telemetry with { Metadata = telemetry.Metadata with { Duration = 1.2 } };
        telemetry = telemetry with { Front = telemetry.Front with { HasGaps = true } };
        telemetry = telemetry with { Front = telemetry.Front with { Segments =
        [
            new ProcessedSuspensionSegment
            {
                FirstDenseIndex = 0,
                StartSeconds = 0.0,
                SampleCount = 2,
            },
            new ProcessedSuspensionSegment
            {
                FirstDenseIndex = 2,
                StartSeconds = 1.0,
                SampleCount = 2,
            },
        ] } };
        var options = new TravelStatisticsOptions(
            Range: new TelemetryTimeRange(0.9, 1.2),
            HistogramMode: TravelDistributionMode.DynamicSag);

        var statistics = TelemetryStatistics.CalculateTravelStatistics(telemetry, SuspensionType.Front, options);

        Assert.Equal(20, statistics.Max);
        Assert.Equal(15, statistics.Average, 6);
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
    public void CalculateBalance_WithSpeedMode_UsesTravelAtPeakStrokeSpeedAndOmitsTrendData()
    {
        var telemetry = CreateBalanceTelemetry(
            frontTravel: [0, 10, 30, 20, 0, 15, 45, 25],
            rearTravel: [0, 20, 50, 30, 0, 25, 55, 35],
            frontVelocity: [10, 200, 800, 100, 5, 100, 600, 200],
            rearVelocity: [10, 300, 500, 100, 5, 250, 700, 200],
            frontCompressions:
            [
                CreateStroke(0, 3, maxVelocity: 800, maxTravel: 30),
                CreateStroke(4, 7, maxVelocity: 600, maxTravel: 45),
            ],
            rearCompressions:
            [
                CreateStroke(0, 3, maxVelocity: 500, maxTravel: 50),
                CreateStroke(4, 7, maxVelocity: 700, maxTravel: 55),
            ]);

        var balance = TelemetryStatistics.CalculateBalance(telemetry,
            BalanceType.Compression,
            new BalanceStatisticsOptions(DisplacementMode: BalanceDisplacementMode.Speed));

        Assert.Collection(
            balance.FrontTravel,
            value => Assert.Equal(30, value, 6),
            value => Assert.Equal(45, value, 6));
        Assert.Collection(
            balance.RearTravel,
            value => Assert.Equal(50, value, 6),
            value => Assert.Equal(55, value, 6));
        Assert.Empty(balance.FrontTrend);
        Assert.Empty(balance.RearTrend);
        Assert.Equal(0, balance.FrontSlope);
        Assert.Equal(0, balance.RearSlope);
        Assert.Equal(0, balance.AbsoluteSlopeDeltaPercent);
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

    [Theory]
    [InlineData(BalanceSpeedMode.LowSpeed, 150, 250, 150, 250)]
    [InlineData(BalanceSpeedMode.HighSpeed, 200, 300, 250, 350)]
    public void CalculateBalance_LowHighSpeedModes_UseSideSpecificCompressionAndReboundThresholds(
        BalanceSpeedMode mode, double frontCompression, double rearCompression, double frontRebound, double rearRebound)
    {
        var telemetry = CreateBalanceTelemetry(
            frontTravel: [0, 10, 20, 30, 40, 50],
            rearTravel: [0, 15, 30, 45, 60, 75],
            frontCompressions:
            [
                CreateStroke(0, 0, maxVelocity: 150, maxTravel: 10),
                CreateStroke(1, 1, maxVelocity: 200, maxTravel: 20),
            ],
            rearCompressions:
            [
                CreateStroke(0, 0, maxVelocity: 250, maxTravel: 15),
                CreateStroke(1, 1, maxVelocity: 300, maxTravel: 30),
            ],
            frontRebounds:
            [
                CreateStroke(2, 2, maxVelocity: -150, maxTravel: 30),
                CreateStroke(3, 3, maxVelocity: -250, maxTravel: 40),
            ],
            rearRebounds:
            [
                CreateStroke(2, 2, maxVelocity: -250, maxTravel: 45),
                CreateStroke(3, 3, maxVelocity: -350, maxTravel: 60),
            ]);
        var options = new BalanceStatisticsOptions(
            SpeedMode: mode,
            FrontCompressionHighSpeedThreshold: 175,
            RearCompressionHighSpeedThreshold: 275,
            FrontReboundHighSpeedThreshold: 225,
            RearReboundHighSpeedThreshold: 325);

        var compression = TelemetryStatistics.CalculateBalance(telemetry, BalanceType.Compression, options);
        var rebound = TelemetryStatistics.CalculateBalance(telemetry, BalanceType.Rebound, options);

        Assert.Equal([frontCompression], compression.FrontVelocity);
        Assert.Equal([rearCompression], compression.RearVelocity);
        Assert.Equal([frontRebound], rebound.FrontVelocity);
        Assert.Equal([rearRebound], rebound.RearVelocity);
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
        Assert.Equal(50, strokePeakHistogram.Values[1][2], 3);
    }

    [Fact]
    public void CalculateVelocityBands_WithStrokePeakAverage_UsesOnePeakPerStroke()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20, 30, 20, 10, 0, 5],
            velocity: [250, 50, 50, 50, -250, -50, -50, -50],
            maxTravel: 100,
            compressions:
            [
                CreateStroke(0, 2, maxVelocity: 250),
                CreateStroke(3, 3, maxVelocity: 50),
            ],
            rebounds:
            [
                CreateStroke(4, 6, maxVelocity: -250),
                CreateStroke(7, 7, maxVelocity: -50),
            ]);

        var sampleBands = TelemetryStatistics.CalculateVelocityBands(
            telemetry,
            SuspensionType.Front,
            new VelocityStatisticsOptions(
                CompressionHighSpeedThreshold: 200.0,
                ReboundHighSpeedThreshold: 200.0));
        var strokePeakBands = TelemetryStatistics.CalculateVelocityBands(
            telemetry,
            SuspensionType.Front,
            new VelocityStatisticsOptions(
                VelocityAverageMode: VelocityAverageMode.StrokePeakAveraged,
                CompressionHighSpeedThreshold: 200.0,
                ReboundHighSpeedThreshold: 200.0));

        Assert.Equal(37.5, sampleBands.LowSpeedCompression, 3);
        Assert.Equal(12.5, sampleBands.HighSpeedCompression, 3);
        Assert.Equal(37.5, sampleBands.LowSpeedRebound, 3);
        Assert.Equal(12.5, sampleBands.HighSpeedRebound, 3);
        Assert.Equal(25, strokePeakBands.LowSpeedCompression, 3);
        Assert.Equal(25, strokePeakBands.HighSpeedCompression, 3);
        Assert.Equal(25, strokePeakBands.LowSpeedRebound, 3);
        Assert.Equal(25, strokePeakBands.HighSpeedRebound, 3);
    }

    [Fact]
    public void CalculateVelocityBands_WithSampleAverage_UsesSeparateCompressionAndReboundThresholds()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20, 30, 20, 10, 0, 5],
            velocity: [150, 250, 450, 50, -150, -250, -450, -50],
            maxTravel: 100,
            compressions:
            [
                CreateStroke(0, 3),
            ],
            rebounds:
            [
                CreateStroke(4, 7),
            ]);
        var options = new VelocityStatisticsOptions(
            CompressionHighSpeedThreshold: 300,
            ReboundHighSpeedThreshold: 200);

        var bands = TelemetryStatistics.CalculateVelocityBands(telemetry, SuspensionType.Front, options);

        Assert.Equal(37.5, bands.LowSpeedCompression, 3);
        Assert.Equal(12.5, bands.HighSpeedCompression, 3);
        Assert.Equal(25, bands.LowSpeedRebound, 3);
        Assert.Equal(25, bands.HighSpeedRebound, 3);
    }

    [Fact]
    public void CalculateVelocityBands_WithStrokePeakAverage_UsesSeparateCompressionAndReboundThresholds()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20, 30],
            maxTravel: 100,
            compressions:
            [
                CreateStroke(0, 0, maxVelocity: 250),
                CreateStroke(1, 1, maxVelocity: 300),
            ],
            rebounds:
            [
                CreateStroke(2, 2, maxVelocity: -150),
                CreateStroke(3, 3, maxVelocity: -200),
            ]);
        var options = new VelocityStatisticsOptions(
            VelocityAverageMode: VelocityAverageMode.StrokePeakAveraged,
            CompressionHighSpeedThreshold: 300,
            ReboundHighSpeedThreshold: 200);

        var bands = TelemetryStatistics.CalculateVelocityBands(telemetry, SuspensionType.Front, options);

        Assert.Equal(25, bands.LowSpeedCompression, 3);
        Assert.Equal(25, bands.HighSpeedCompression, 3);
        Assert.Equal(25, bands.LowSpeedRebound, 3);
        Assert.Equal(25, bands.HighSpeedRebound, 3);
    }

    [Fact]
    public void CalculateVelocityBands_ExactThresholdValues_AreHighSpeed()
    {
        var telemetry = CreateTelemetry(
            travel: [0, 10, 20, 30],
            velocity: [199, 200, -199, -200],
            maxTravel: 100,
            compressions:
            [
                CreateStroke(0, 1),
            ],
            rebounds:
            [
                CreateStroke(2, 3),
            ]);

        var bands = TelemetryStatistics.CalculateVelocityBands(
            telemetry,
            SuspensionType.Front,
            new VelocityStatisticsOptions(
                CompressionHighSpeedThreshold: 200,
                ReboundHighSpeedThreshold: 200));

        Assert.Equal(25, bands.LowSpeedCompression, 3);
        Assert.Equal(25, bands.HighSpeedCompression, 3);
        Assert.Equal(25, bands.LowSpeedRebound, 3);
        Assert.Equal(25, bands.HighSpeedRebound, 3);
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

    [Theory]
    [InlineData(0)] // Legacy dense only.
    [InlineData(1)] // Canonical segments only.
    [InlineData(2)] // Legacy dense plus segments.
    public void CalculateVibration_WithContinuousImu_ClassifiesKnownTimeShares(int representation)
    {
        var imu = CreateImuData(ImuLocation.Fork, sampleRate: 1000, sampleCount: 2000, vibrationG: 1);
        if (representation != 0)
        {
            imu = imu with { Segments = [new RawImuSegment { LocationId = (byte)ImuLocation.Fork, Records = imu.Records }] };
        }
        if (representation == 1) imu = imu with { Records = [] };
        var telemetry = CreateTelemetry(Enumerable.Repeat(10.0, 400).ToArray(), 100, 200,
            compressions: [CreateStroke(0, 99)], rebounds: [CreateStroke(100, 199)], imuData: imu);

        var stats = TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(25, stats.CompressionPercent, 9);
        Assert.Equal(25, stats.ReboundPercent, 9);
        Assert.Equal(50, stats.OtherPercent, 9);
        Assert.Equal(1, stats.AverageGOverall, 9);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void CalculateVibration_MagicCarpetUsesMovementPerGSecond_IndependentOfImuRate(int imuRate)
    {
        var telemetry = CreateTelemetry(
            travel: [0, 100, 0, 100, 0, 100, 0], maxTravel: 100, sampleRate: 1,
            compressions: [CreateStroke(0, 1)],
            imuData: CreateImuData(ImuLocation.Fork, sampleRate: imuRate, sampleCount: 7 * imuRate, vibrationG: 1));

        var stats = TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        // 600 mm of movement and 7 seconds at 1 g of vibration.
        Assert.Equal(600.0 / 7, stats.MagicCarpet, 9);
    }

    [Fact]
    public void CalculateVibration_WithSegmentedImu_SkipsSamplesInTravelGaps()
    {
        var compression = CreateStroke(0, 1);
        compression = compression with { StartSeconds = 0.0 };
        compression = compression with { EndSeconds = 0.1 };
        var telemetry = new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 10, Duration = 1.2 },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 100,
                Travel = [0, 10, 20, 30],
                Velocity = [0, 100, 0, 100],
                HasGaps = true,
                Segments =
                [
                    new ProcessedSuspensionSegment
                    {
                        FirstDenseIndex = 0,
                        FirstSourceIndex = 0,
                        StartSeconds = 0.0,
                        SampleCount = 2,
                    },
                    new ProcessedSuspensionSegment
                    {
                        FirstDenseIndex = 2,
                        FirstSourceIndex = 10,
                        StartSeconds = 1.0,
                        SampleCount = 2,
                    }
                ],
                Strokes = new Strokes { Compressions = [compression], Rebounds = [] },
                TravelBins = [.. (CreateTravelBins(100))],
                VelocityBins = [-100, 0, 100],
            },
            Rear = new Suspension { Strokes = new Strokes(), Travel = [], Velocity = [], TravelBins = [], VelocityBins = [] },
            Airtimes = [],
            ImuData = new RawImuData
            {
                SampleRate = 10,
                ActiveLocations = [(byte)ImuLocation.Fork],
                Meta = [new ImuMetaEntry((byte)ImuLocation.Fork, 1000, 1)],
                HasGaps = true,
                Segments =
                [
                    new RawImuSegment
                    {
                        LocationId = (byte)ImuLocation.Fork,
                        FirstIndex = 0,
                        FirstMonotonicDeltaUs = 0,
                        Records =
                        [
                            new ImuRecord(0, 0, 2000, 0, 0, 0),
                            new ImuRecord(0, 0, 2000, 0, 0, 0),
                        ],
                    },
                    new RawImuSegment
                    {
                        LocationId = (byte)ImuLocation.Fork,
                        FirstIndex = 5,
                        FirstMonotonicDeltaUs = 500_000,
                        Records = [new ImuRecord(0, 0, 2000, 0, 0, 0)],
                    }
                ],
            },
        };

        var stats = TelemetryStatistics.CalculateVibration(telemetry, ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(100.0, stats!.CompressionPercent);
        Assert.Equal(0.0, stats.OtherPercent);
    }

    [Fact]
    public void CalculateVibration_WithSegmentedData_PreservesInclusiveStrokeEndsAndCompressionPrecedence()
    {
        var compression = CreateStroke(0, 2);
        compression = compression with { StartSeconds = 0.0 };
        compression = compression with { EndSeconds = 0.2 };
        var rebound = CreateStroke(1, 3);
        rebound = rebound with { StartSeconds = 0.1 };
        rebound = rebound with { EndSeconds = 0.3 };
        var imuData = CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 4, vibrationG: 1);
        imuData = imuData with { Segments = [
            new RawImuSegment
            {
                LocationId = (byte)ImuLocation.Fork,
                FirstMonotonicDeltaUs = 0,
                Records = imuData.Records,
            },
        ] };
        imuData = imuData with { HasGaps = true };
        var telemetry = CreateTelemetry(
            travel: [10, 20, 30, 40],
            maxTravel: 100,
            sampleRate: 10,
            compressions: [compression],
            rebounds: [rebound],
            imuData: imuData);
        telemetry = telemetry with { Front = telemetry.Front with { Segments =
        [
            new ProcessedSuspensionSegment
            {
                FirstDenseIndex = 0,
                StartSeconds = 0,
                SampleCount = 4,
            },
        ] } };
        telemetry = telemetry with { Front = telemetry.Front with { HasGaps = true } };

        var stats = TelemetryStatistics.CalculateVibration(
            telemetry,
            ImuLocation.Fork,
            SuspensionType.Front);

        Assert.NotNull(stats);
        Assert.Equal(75, stats.CompressionPercent, 6);
        Assert.Equal(25, stats.ReboundPercent, 6);
        Assert.Equal(0, stats.OtherPercent, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CalculateVibration_WithLegacyUnorderedInputs_ClassifiesKnownSamples(int ordering)
    {
        static Stroke TimedStroke(int start, int end, double startSeconds, double endSeconds)
        {
            var stroke = CreateStroke(start, end);
            stroke = stroke with { StartSeconds = startSeconds };
            stroke = stroke with { EndSeconds = endSeconds };
            return stroke;
        }

        static TelemetryData CreateSegmentedTelemetry(int ordering)
        {
            var firstImu = new RawImuSegment
            {
                LocationId = (byte)ImuLocation.Fork,
                FirstIndex = 0,
                FirstMonotonicDeltaUs = 0,
                Records =
                [
                    new ImuRecord(0, 0, 1800, 0, 0, 0),
                    new ImuRecord(0, 0, 1900, 0, 0, 0),
                    new ImuRecord(0, 0, 2000, 0, 0, 0),
                ],
            };
            var secondImu = new RawImuSegment
            {
                LocationId = (byte)ImuLocation.Fork,
                FirstIndex = 10,
                FirstMonotonicDeltaUs = 1_000_000,
                Records =
                [
                    new ImuRecord(0, 0, 2100, 0, 0, 0),
                    new ImuRecord(0, 0, 2200, 0, 0, 0),
                    new ImuRecord(0, 0, 2300, 0, 0, 0),
                ],
            };
            var firstTravel = new ProcessedSuspensionSegment
            {
                FirstDenseIndex = 0,
                FirstSourceIndex = 0,
                StartSeconds = 0,
                SampleCount = 3,
            };
            var secondTravel = new ProcessedSuspensionSegment
            {
                FirstDenseIndex = 3,
                FirstSourceIndex = 10,
                StartSeconds = 1,
                SampleCount = 3,
            };
            var firstCompression = TimedStroke(0, 1, 0, 0.1);
            var secondCompression = TimedStroke(3, 4, 1, 1.1);
            var firstRebound = TimedStroke(1, 2, 0.1, 0.2);
            var secondRebound = TimedStroke(4, 5, 1.1, 1.2);
            return new TelemetryData
            {
                Metadata = new Metadata { SampleRate = 10, Duration = 1.3 },
                Front = new Suspension
                {
                    Present = true,
                    MaxTravel = 100,
                    Travel = [10, 20, 30, 70, 80, 90],
                    Velocity = [.. (new double[6])],
                    HasGaps = true,
                    Segments = ordering == 2 ? [secondTravel, firstTravel] : [firstTravel, secondTravel],
                    Strokes = new Strokes
                    {
                        Compressions = ordering == 3 ? [secondCompression, firstCompression] : [firstCompression, secondCompression],
                        Rebounds = ordering == 3 ? [secondRebound, firstRebound] : [firstRebound, secondRebound],
                    },
                    TravelBins = [.. (CreateTravelBins(100))],
                    VelocityBins = [-100, 0, 100],
                },
                Rear = CreateSuspension([], 100, [], []),
                Airtimes = [],
                ImuData = new RawImuData
                {
                    SampleRate = 10,
                    ActiveLocations = [(byte)ImuLocation.Fork],
                    Meta = [new ImuMetaEntry((byte)ImuLocation.Fork, 1000, 1)],
                    HasGaps = true,
                    Segments = ordering == 1 ? [secondImu, firstImu] : [firstImu, secondImu],
                },
            };
        }

        var stats = TelemetryStatistics.CalculateVibration(
            CreateSegmentedTelemetry(ordering), ImuLocation.Fork, SuspensionType.Front);

        Assert.NotNull(stats);
        // Compression samples contribute .8 + .9 + 1.1 + 1.2 g; rebound contributes 1 + 1.3 g.
        Assert.Equal(4.0 / 6.3 * 100, stats.CompressionPercent, 9);
        Assert.Equal(2.3 / 6.3 * 100, stats.ReboundPercent, 9);
        Assert.Equal(0, stats.OtherPercent, 9);
        Assert.Equal(1.05, stats.AverageGOverall, 9);
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

    [Theory]
    [InlineData(VibrationNullCase.WithoutImuData)]
    [InlineData(VibrationNullCase.InactiveLocation)]
    [InlineData(VibrationNullCase.WithoutPairedStrokeData)]
    [InlineData(VibrationNullCase.InvalidScale)]
    [InlineData(VibrationNullCase.MissingMeta)]
    [InlineData(VibrationNullCase.MissingTravelSamples)]
    [InlineData(VibrationNullCase.ZeroMaxTravel)]
    [InlineData(VibrationNullCase.ZeroTelemetrySampleRate)]
    [InlineData(VibrationNullCase.ZeroImuSampleRate)]
    public void CalculateVibration_WithMissingPrerequisite_ReturnsNull(VibrationNullCase missingPrerequisite)
    {
        var (telemetry, location) = CreateVibrationNullCase(missingPrerequisite);

        if (missingPrerequisite is VibrationNullCase.WithoutImuData or VibrationNullCase.InactiveLocation)
        {
            Assert.False(TelemetryStatistics.HasVibrationData(telemetry, location));
        }

        Assert.Null(TelemetryStatistics.CalculateVibration(telemetry, location, SuspensionType.Front));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(1000)]
    public void CalculateTravelFrequencyHistogram_UsesSameFrequencyResolutionAcrossSampleRates(int sampleRate)
    {
        var telemetry = CreateTelemetry(
            travel: Enumerable.Range(0, sampleRate).Select(index => Math.Sin(index * 0.1)).ToArray(),
            maxTravel: 100,
            sampleRate: sampleRate);

        var histogram = TelemetryStatistics.CalculateTravelFrequencyHistogram(telemetry, SuspensionType.Front);

        Assert.True(histogram.Bins.Count > 2);
        Assert.Equal(0.05, histogram.Bins[1] - histogram.Bins[0], precision: 6);
    }

    private static RawTelemetryData CreateSegmentedRaw(params RawCountSegment[] frontSegments) => new()
    {
        Version = 5,
        SampleRate = 100,
        FrontSegments = frontSegments,
        Front = frontSegments.SelectMany(segment => segment.Counts).ToArray(),
        StreamGaps =
        [
            new RawStreamGap
            {
                StreamKind = 1,
                FirstMissingIndex = 50,
                MissingCount = 50,
                MissingTimeUs = 500_000,
                Reason = "index_gap",
            }
        ],
    };

    private static TelemetryData ProcessV5Segments(double duration, params RawCountSegment[] frontSegments) =>
        TelemetryData.FromRecording(
            CreateSegmentedRaw(frontSegments),
            new Metadata { SampleRate = 100, Duration = duration },
            new BikeData(100, null, value => value, null),
            new TelemetryProcessingOptions(0));

    private static byte[] CreateLegacySegmentedTelemetryBlob(
        double[] frontTravel,
        double[] frontVelocity,
        bool hasGaps,
        LegacyProcessedSuspensionSegment[] segments)
    {
        var legacy = new LegacyTelemetryData
        {
            Metadata = new Metadata { SampleRate = 10, Duration = frontTravel.Length / 10.0 },
            Front = new LegacySuspension
            {
                Present = true,
                MaxTravel = 100,
                Travel = frontTravel,
                Velocity = frontVelocity,
                Strokes = new Strokes(),
                TravelBins = CreateTravelBins(100),
                VelocityBins = [-100, 0, 100],
                Segments = segments,
                HasGaps = hasGaps,
            },
            Rear = new LegacySuspension
            {
                Present = false,
                Travel = [],
                Velocity = [],
                Strokes = new Strokes(),
                TravelBins = [],
                VelocityBins = [],
                Segments = [],
            },
            Airtimes = [],
        };

        return MessagePackSerializer.Serialize(legacy);
    }

    [MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
    internal sealed class LegacyTelemetryData
    {
        public Metadata Metadata { get; set; } = new();
        public LegacySuspension Front { get; set; } = new();
        public LegacySuspension Rear { get; set; } = new();
        public Airtime[] Airtimes { get; set; } = [];
        public MarkerData[] Markers { get; set; } = [];
        public RawImuData? ImuData { get; set; }
        public GpsRecord[]? GpsData { get; set; }
        public TemperatureAverage[] TemperatureAverages { get; set; } = [];
        public RawStreamGap[] StreamGaps { get; set; } = [];
        public SstFinalStatus? FinalStatus { get; set; }
        public bool MissingFinalStatus { get; set; }
    }

    [MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
    internal sealed class LegacySuspension
    {
        public bool Present { get; set; }
        public double AnomalyRate { get; set; }
        public double? MaxTravel { get; set; }
        public double[] Travel { get; set; } = [];
        public double[] Velocity { get; set; } = [];
        public Strokes Strokes { get; set; } = new();
        public double[] TravelBins { get; set; } = [];
        public double[] VelocityBins { get; set; } = [];
        public double[] FineVelocityBins { get; set; } = [];
        public LegacyProcessedSuspensionSegment[] Segments { get; set; } = [];
        public bool HasGaps { get; set; }
    }

    [MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
    internal sealed class LegacyProcessedSuspensionSegment
    {
        public int FirstDenseIndex { get; set; }
        public ulong FirstSourceIndex { get; set; }
        public double StartSeconds { get; set; }
        public double[] Travel { get; set; } = [];
        public double[] Velocity { get; set; } = [];
    }

    private static TelemetryData CreateTelemetry(
        double[] travel,
        double maxTravel,
        int sampleRate = 1000,
        Stroke[]? compressions = null,
        Stroke[]? rebounds = null,
        RawImuData? imuData = null,
        double[]? velocity = null)
    {
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = sampleRate, Duration = travel.Length / (double)sampleRate },
            Front = CreateSuspension(travel, maxTravel, compressions, rebounds, velocity),
            Rear = CreateSuspension(travel, maxTravel, [], []),
            Airtimes = [],
            Markers = [],
            ImuData = imuData,
        };
    }

    private static TelemetryData CreateBalanceTelemetry(
        double[] frontTravel,
        double[] rearTravel,
        Stroke[]? frontCompressions,
        Stroke[]? rearCompressions,
        Stroke[]? frontRebounds = null,
        Stroke[]? rearRebounds = null,
        double[]? frontVelocity = null,
        double[]? rearVelocity = null,
        double maxTravel = 100)
    {
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = frontTravel.Length / 1000.0 },
            Front = CreateSuspension(frontTravel, maxTravel, frontCompressions ?? [], frontRebounds ?? [], frontVelocity),
            Rear = CreateSuspension(rearTravel, maxTravel, rearCompressions ?? [], rearRebounds ?? [], rearVelocity),
            Airtimes = [],
            Markers = [],
        };
    }

    private static Suspension CreateSuspension(
        double[] travel,
        double maxTravel,
        Stroke[]? compressions,
        Stroke[]? rebounds,
        double[]? velocity = null)
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = maxTravel,
            Travel = [.. (travel)],
            Velocity = [.. (velocity ?? new double[travel.Length])],
            TravelBins = [.. (CreateTravelBins(maxTravel))],
            VelocityBins = [-100, 0, 100],
            Strokes = new Strokes
            {
                Compressions = [.. (compressions ?? [])],
                Rebounds = [.. (rebounds ?? [])],
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
            DigitizedTravel = [.. (digitizedTravel ?? [])],
            DigitizedVelocity = [.. (digitizedVelocity ?? [])],
        };
    }

    private static (TelemetryData Telemetry, ImuLocation Location) CreateVibrationNullCase(
        VibrationNullCase missingPrerequisite)
    {
        var telemetry = CreateTelemetry(
            travel: missingPrerequisite == VibrationNullCase.MissingTravelSamples ? [10] : [0, 10],
            maxTravel: missingPrerequisite == VibrationNullCase.ZeroMaxTravel ? 0 : 100,
            compressions: missingPrerequisite == VibrationNullCase.WithoutPairedStrokeData
                ? null
                : [CreateStroke(0, missingPrerequisite == VibrationNullCase.MissingTravelSamples ? 0 : 1)],
            imuData: missingPrerequisite == VibrationNullCase.WithoutImuData
                ? null
                : CreateImuData(ImuLocation.Fork, sampleRate: 10, sampleCount: 10, vibrationG: 1));

        switch (missingPrerequisite)
        {
            case VibrationNullCase.InactiveLocation:
                return (telemetry, ImuLocation.Frame);
            case VibrationNullCase.InvalidScale:
                telemetry = telemetry with { ImuData = telemetry.ImuData! with { Meta = [new ImuMetaEntry((byte)ImuLocation.Fork, 0, 1.0f)] } };
                break;
            case VibrationNullCase.MissingMeta:
                telemetry = telemetry with { ImuData = telemetry.ImuData! with { Meta = [] } };
                break;
            case VibrationNullCase.ZeroTelemetrySampleRate:
                telemetry = telemetry with { Metadata = telemetry.Metadata with { SampleRate = 0 } };
                break;
            case VibrationNullCase.ZeroImuSampleRate:
                telemetry = telemetry with { ImuData = telemetry.ImuData! with { SampleRate = 0 } };
                break;
        }

        return (telemetry, ImuLocation.Fork);
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
            Records = [.. (Enumerable.Range(0, sampleCount)
                .Select(_ => new ImuRecord(0, 0, az, 0, 0, 0))
                .ToList())],
        };
    }

    public enum VibrationNullCase
    {
        WithoutImuData,
        InactiveLocation,
        WithoutPairedStrokeData,
        InvalidScale,
        MissingMeta,
        MissingTravelSamples,
        ZeroMaxTravel,
        ZeroTelemetrySampleRate,
        ZeroImuSampleRate
    }
}
