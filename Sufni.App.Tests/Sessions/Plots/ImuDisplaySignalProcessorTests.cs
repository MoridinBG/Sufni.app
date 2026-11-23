using Sufni.App.Sessions.Plots;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.Sessions.Plots;

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

        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal(3, result.FramePitchRoll!.PitchDegrees.Length);
        Assert.All(result.FramePitchRoll.PitchDegrees, pitch => Assert.Equal(10.018, pitch, precision: 2));
        Assert.All(result.FramePitchRoll.RollDegrees, roll => Assert.InRange(roll, -0.1, 0.1));
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

        Assert.NotNull(result.FramePitchRoll);
        Assert.Equal([0.0, 0.1, 1.0], result.FramePitchRoll!.Times);
        Assert.Equal(10.018, result.FramePitchRoll.PitchDegrees[0], precision: 2);
        Assert.Equal(10.018, result.FramePitchRoll.PitchDegrees[1], precision: 2);
        Assert.Equal(0.0, result.FramePitchRoll.PitchDegrees[2], precision: 6);
    }

    private static RawImuData CreateRawImuData(
        IReadOnlyList<byte> activeLocations,
        IReadOnlyList<ImuRecord> records,
        IReadOnlyList<ImuMetaEntry>? meta = null)
    {
        return new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = [.. activeLocations],
            Meta = meta is not null ? [.. meta] :
            [
                new ImuMetaEntry((byte)ImuLocation.Frame, 10, 100),
                new ImuMetaEntry((byte)ImuLocation.Fork, 10, 100),
            ],
            Records = [.. records],
        };
    }

    private static ImuRecord FrameLevel() => new(0, 0, 1000, 0, 0, 0);

    private static ImuRecord FramePitch10Degrees() => new(174, 0, 985, 0, 0, 0);

    private static ImuRecord FrameRest10() => new(0, 0, 10, 0, 0, 0);

    private static ImuRecord ForkLevel() => new(0, 0, 1000, 0, 0, 0);
}
