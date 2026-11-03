using Sufni.Telemetry;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.LiveDaq.Services.Imu;

public class LiveImuDisplaySignalProcessorTests
{
    [Fact]
    public void LiveProcessor_EmitsImmediatelyWithoutRestCalibration()
    {
        var sut = new LiveImuDisplaySignalProcessor();

        var first = sut.ProcessBatch(
            [
                new LiveImuDisplayInputSeries(
                    LiveImuLocation.Frame,
                    [0.0, 0.1],
                    [new ImuRecord(174, 0, 985, 0, 0, 0), new ImuRecord(174, 0, 985, 0, 0, 0)],
                    AccelLsbPerG: 1000,
                    GyroLsbPerDps: 100),
            ],
            sampleRateHz: 10);

        var second = sut.ProcessBatch(
            [
                new LiveImuDisplayInputSeries(
                    LiveImuLocation.Frame,
                    [0.2],
                    [new ImuRecord(0, 0, 1000, 0, 0, 0)],
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

}
