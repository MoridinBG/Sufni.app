using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.LiveDaq.Services.Imu;

public class ImuDisplaySignalProcessorTests
{
    [Fact]
    public void ProcessRecorded_UsesFirmwareCalibratedBikeFrameWithoutRestBaseline()
    {
        var data = CreateRawImuData(
            activeLocations: [(byte)ImuLocation.Frame, (byte)ImuLocation.Fork],
            meta:
            [
                new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 100),
                new ImuMetaEntry((byte)ImuLocation.Fork, 1000, 100),
            ],
            records:
            [
                FramePitch10Degrees(), ForkLevel(),
                FramePitch10Degrees(), ForkLevel(),
                FramePitch10Degrees(), ForkLevel(),
            ]);

        var result = ImuDisplaySignalProcessor.ProcessRecorded(data);

        Assert.Equal([(byte)ImuLocation.Frame, (byte)ImuLocation.Fork], result.VibrationSeries.Select(series => series.LocationId).ToArray());
        Assert.All(result.VibrationSeries, series =>
        {
            Assert.Equal(3, series.Times.Length);
            Assert.Equal(3, series.RmsG.Length);
        });
        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal(3, result.FramePitchRoll!.Times.Length);
        Assert.All(result.FramePitchRoll.PitchDegrees, pitch => Assert.InRange(pitch, 9.5, 10.5));
        Assert.All(result.FramePitchRoll.RollDegrees, roll => Assert.InRange(roll, -0.1, 0.1));
    }

    [Fact]
    public void ProcessRecorded_FusesGyroPredictionWithAccelerometerCorrection()
    {
        var data = CreateRawImuData(
            activeLocations: [(byte)ImuLocation.Frame],
            meta: [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 100)],
            records:
            [
                FrameLevel(),
                new ImuRecord(0, 0, 1000, 0, 1000, 0),
            ]);

        var result = ImuDisplaySignalProcessor.ProcessRecorded(data);

        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal(0.0, result.FramePitchRoll!.PitchDegrees[0], precision: 6);
        Assert.InRange(result.FramePitchRoll.PitchDegrees[1], 0.7, 0.95);
    }

    [Fact]
    public void ProcessRecorded_SkipsAccelerometerCorrection_WhenAccelerationMagnitudeIsNotGravityLike()
    {
        var data = CreateRawImuData(
            activeLocations: [(byte)ImuLocation.Frame],
            meta: [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 100)],
            records:
            [
                FrameLevel(),
                new ImuRecord(800, 0, 1000, 0, 1000, 0),
            ]);

        var result = ImuDisplaySignalProcessor.ProcessRecorded(data);

        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal(0.0, result.FramePitchRoll!.PitchDegrees[0], precision: 6);
        Assert.Equal(1.0, result.FramePitchRoll.PitchDegrees[1], precision: 6);
    }

    [Fact]
    public void ProcessRecorded_SkipsAccelerometerCorrection_DuringRecordedAirtime()
    {
        var telemetry = CreateMinimal(
            records:
            [
                FrameLevel(),
                new ImuRecord(0, 0, 1000, 0, 3000, 0),
                new ImuRecord(0, 0, 1000, 0, 3000, 0),
            ],
            airtimes: [new Airtime { Start = 0.05, End = 0.25 }]);

        var result = ImuDisplaySignalProcessor.ProcessRecorded(telemetry);

        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal(0.0, result.FramePitchRoll!.PitchDegrees[0], precision: 6);
        Assert.Equal(3.0, result.FramePitchRoll.PitchDegrees[1], precision: 6);
        Assert.Equal(6.0, result.FramePitchRoll.PitchDegrees[2], precision: 6);
    }

    [Fact]
    public void ProcessRecorded_SkipsAccelerometerCorrection_WhenSuspensionVelocityIsHigh()
    {
        var telemetry = CreateMinimal(
            records:
            [
                FrameLevel(),
                new ImuRecord(0, 0, 1000, 0, 3000, 0),
                new ImuRecord(0, 0, 1000, 0, 3000, 0),
            ],
            frontVelocity: [0, 500, 500]);

        var result = ImuDisplaySignalProcessor.ProcessRecorded(telemetry);

        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal(0.0, result.FramePitchRoll!.PitchDegrees[0], precision: 6);
        Assert.Equal(3.0, result.FramePitchRoll.PitchDegrees[1], precision: 6);
        Assert.Equal(6.0, result.FramePitchRoll.PitchDegrees[2], precision: 6);
    }

    [Fact]
    public void CorrectionWeightAt_WithGappedSuspension_SamplesVelocityByRealTimeNotDenseIndex()
    {
        // Front travel has a time gap: segment 0 sits at t=0 (low velocity) and segment 1 at
        // t=1.0 (high velocity). The dense Velocity array is just the concatenation
        // [0, 0, 500, 500], so a dense-index lookup at t=1.0 (index 10) lands out of bounds and
        // misses the high velocity. The attitude correction must sample by real time instead.
        var telemetry = new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 10, Duration = 1.2 },
            Front = new Suspension
            {
                Present = true,
                HasGaps = true,
                Travel = [0, 0, 0, 0],
                Velocity = [0, 0, 500, 500],
                Strokes = new Strokes(),
                Segments =
                [
                    new ProcessedSuspensionSegment { FirstDenseIndex = 0, StartSeconds = 0.0, SampleCount = 2 },
                    new ProcessedSuspensionSegment { FirstDenseIndex = 2, StartSeconds = 1.0, SampleCount = 2 },
                ],
            },
            Rear = new Suspension { Present = false, Strokes = new Strokes() },
            Airtimes = [],
        };

        var context = AttitudeCorrectionContext.CreateRecorded(telemetry);

        // Low-velocity first segment: accelerometer correction is applied.
        Assert.Equal(1.0, context.CorrectionWeightAt(0.0));
        // Inside the gap there is no sample, so correction stays enabled.
        Assert.Equal(1.0, context.CorrectionWeightAt(0.5));
        // Real time 1.0 falls in the high-velocity second segment: correction is suppressed.
        Assert.Equal(0.0, context.CorrectionWeightAt(1.0));
    }

    [Fact]
    public void ProcessRecorded_SuppressesPitchRoll_WhenFrameGyroScaleIsInvalid()
    {
        var data = CreateRawImuData(
            activeLocations: [(byte)ImuLocation.Frame],
            meta: [new ImuMetaEntry((byte)ImuLocation.Frame, 10, 0)],
            records:
            [
                FrameRest10(),
                new ImuRecord(2, 0, 10, 0, 30, 0),
            ]);

        var result = ImuDisplaySignalProcessor.ProcessRecorded(data);

        Assert.Single(result.VibrationSeries);
        Assert.Null(result.FramePitchRoll);
    }

    [Fact]
    public void ProcessRecorded_WithSegments_UsesSegmentTimesWithoutLegacyRecords()
    {
        var data = new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = [(byte)ImuLocation.Frame],
            Meta = [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 100)],
            Records = [],
            HasGaps = true,
            Segments =
            [
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Records = [FramePitch10Degrees(), FramePitch10Degrees()],
                },
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstIndex = 20,
                    FirstMonotonicDeltaUs = 1_000_000,
                    Records = [FrameLevel()],
                }
            ],
        };

        var result = ImuDisplaySignalProcessor.ProcessRecorded(data);

        var vibration = Assert.Single(result.VibrationSeries);
        Assert.Equal([0.0, 0.1, 1.0], vibration.Times);
        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal([0.0, 0.1, 1.0], result.FramePitchRoll!.Times);
        Assert.InRange(result.FramePitchRoll.PitchDegrees[0], 9.5, 10.5);
        Assert.InRange(result.FramePitchRoll.PitchDegrees[1], 9.5, 10.5);
        Assert.Equal(0.0, result.FramePitchRoll.PitchDegrees[2], precision: 6);
    }

    [Fact]
    public void LiveProcessor_EmitsImmediatelyWithoutRestCalibration()
    {
        var sut = new LiveImuDisplaySignalProcessor();

        var first = sut.ProcessBatch(
            [
                new LiveImuDisplayInputSeries(
                    LiveImuLocation.Frame,
                    [0.0, 0.1],
                    [FramePitch10Degrees(), FramePitch10Degrees()],
                    AccelLsbPerG: 1000,
                    GyroLsbPerDps: 100),
            ],
            sampleRateHz: 10);

        var second = sut.ProcessBatch(
            [
                new LiveImuDisplayInputSeries(
                    LiveImuLocation.Frame,
                    [0.2],
                    [FrameLevel()],
                    AccelLsbPerG: 1000,
                    GyroLsbPerDps: 100),
            ],
            sampleRateHz: 10);

        Assert.True(second.VibrationTimes.TryGetValue(LiveImuLocation.Frame, out var vibrationTimes));
        Assert.True(first.VibrationTimes.TryGetValue(LiveImuLocation.Frame, out var firstVibrationTimes));
        Assert.Equal(2, firstVibrationTimes.Count);
        Assert.Equal(2, first.VibrationRms[LiveImuLocation.Frame].Count);
        Assert.NotNull(first.FramePitchRoll);
        Assert.Equal(2, first.FramePitchRoll!.Times.Length);
        Assert.InRange(first.FramePitchRoll.PitchDegrees[0], 9.5, 10.5);
        Assert.Single(vibrationTimes);
        Assert.Single(second.VibrationRms[LiveImuLocation.Frame]);
    }

    private static RawImuData CreateRawImuData(
        IReadOnlyList<byte> activeLocations,
        IReadOnlyList<ImuRecord> records,
        IReadOnlyList<ImuMetaEntry>? meta = null)
    {
        return new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = activeLocations.ToList(),
            Meta = meta?.ToList() ??
            [
                new ImuMetaEntry((byte)ImuLocation.Frame, 10, 100),
                new ImuMetaEntry((byte)ImuLocation.Fork, 10, 100),
            ],
            Records = records.ToList(),
        };
    }

    private static TelemetryData CreateMinimal(
        IReadOnlyList<ImuRecord> records,
        IReadOnlyList<Airtime>? airtimes = null,
        IReadOnlyList<double>? frontVelocity = null)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                Duration = records.Count / 10.0,
                SampleRate = 10,
            },
            Front = new Suspension
            {
                Present = true,
                Travel = Enumerable.Repeat(0.0, records.Count).ToArray(),
                Velocity = frontVelocity?.ToArray() ?? Enumerable.Repeat(0.0, records.Count).ToArray(),
                Strokes = new Strokes(),
            },
            Rear = new Suspension
            {
                Present = true,
                Travel = Enumerable.Repeat(0.0, records.Count).ToArray(),
                Velocity = Enumerable.Repeat(0.0, records.Count).ToArray(),
                Strokes = new Strokes(),
            },
            Airtimes = airtimes?.ToArray() ?? [],
            ImuData = CreateRawImuData(
                activeLocations: [(byte)ImuLocation.Frame],
                meta: [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 100)],
                records: records),
        };
    }

    private static ImuRecord FrameLevel() => new(0, 0, 1000, 0, 0, 0);

    private static ImuRecord FramePitch10Degrees() => new(174, 0, 985, 0, 0, 0);

    private static ImuRecord FrameRest10() => new(0, 0, 10, 0, 0, 0);

    private static ImuRecord ForkLevel() => new(0, 0, 1000, 0, 0, 0);
}
