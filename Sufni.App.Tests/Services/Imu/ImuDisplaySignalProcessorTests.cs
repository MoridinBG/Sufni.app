using Sufni.App.Services.Imu;
using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.Imu;

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

    private static ImuRecord FrameLevel() => new(0, 0, 1000, 0, 0, 0);

    private static ImuRecord FramePitch10Degrees() => new(174, 0, 985, 0, 0, 0);

    private static ImuRecord FrameRest10() => new(0, 0, 10, 0, 0, 0);

    private static ImuRecord ForkLevel() => new(0, 0, 1000, 0, 0, 0);
}
