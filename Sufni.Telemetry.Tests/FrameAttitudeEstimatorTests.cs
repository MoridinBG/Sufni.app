using System.Numerics;

namespace Sufni.Telemetry.Tests;

public class FrameAttitudeEstimatorTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public void StaticTiltUsesPositiveNoseUpPitch(double degrees)
    {
        var ride = SyntheticRide.Build([Quiet(70, -degrees)]);
        Assert.All(Recorded(ride), a => Assert.InRange(a.PitchDegrees, degrees - 0.1, degrees + 0.1));
    }

    [Fact]
    public void RotationAboutPositiveBikeYTipsTheNoseDown()
    {
        Quaternion? rotation = null;
        for (var i = 0; i <= 200; i++)
        {
            var sample = PitchedImuRecord.At(-10 * i / 200d, 4096, pitchRateDps: -10, gyroLsbPerDps: 16);
            rotation = FrameAttitudeEstimator.Advance(rotation, sample, 1d / 200, 4096, 16);
        }
        Assert.InRange(FrameAttitudeEstimator.Read(rotation!.Value).PitchDegrees, -10.1, -9.9);
    }

    [Fact]
    public void LeanedTurnKeepsTheFramePitchLevel()
    {
        var ride = SyntheticRide.Build([Quiet(210, 0) with { TurnRadius = 22 }],
            new RideRecording(Roll: 20));
        var attitudes = Recorded(ride, aided: true);
        Assert.All(attitudes, a => Assert.InRange(a.PitchDegrees, -1, 1));
        Assert.InRange(attitudes[^1].RollDegrees, 19, 21);
    }

    [Fact]
    public void ShakenDescentRetainsItsMeanInclination()
    {
        var ride = SyntheticRide.Build([Quiet(210, 10)], new RideRecording(ShakeG: 2));
        var interior = Recorded(ride).Skip(2000).Take(2000);
        Assert.InRange(interior.Average(a => a.PitchDegrees), -10.5, -9.5);
    }

    [Fact]
    public void PitchChangeShowsWithinHalfASecondOnEachSide()
    {
        const int rate = 200;
        var attitudes = Recorded(SyntheticRide.Build([Quiet(70, 0), Quiet(70, 15)]));
        Assert.InRange(attitudes[(int)(9.5 * rate)].PitchDegrees, -1, 1);
        Assert.InRange(attitudes[(int)(10.5 * rate)].PitchDegrees, -16, -14);
    }

    [Fact]
    public void OriginalFixAccelerationRemovesBrakingBias()
    {
        const int rate = 200;
        var ride = SyntheticRide.BrakingDescent();
        var window = SyntheticRide.BrakingDescentWindow;
        double DuringBraking(bool aided) => Recorded(ride, aided).Skip((int)(window.StartSeconds * rate))
            .Take((int)(window.DurationSeconds * rate)).Average(a => a.PitchDegrees);
        Assert.InRange(DuringBraking(true), -12, -8);
        Assert.True(Math.Abs(DuringBraking(false) + 10) > 2);
    }

    private static RideSection Quiet(double length, double grade) => new(length, GradeDegrees: grade,
        LowBandG: 0, MiddleBandG: 0, HighBandG: 0);

    private static FrameAttitude[] Recorded(SyntheticRide ride, bool aided = false)
    {
        var imu = ride.Telemetry.ImuData!;
        var track = aided ? TrackMotion.FromFixes(ride.Fixes, ride.Telemetry.Metadata.Duration) : null;
        return FrameAttitudeEstimator.Recorded(imu.SampleSegments[0], imu.SampleRate, imu.Meta[0], track);
    }
}
