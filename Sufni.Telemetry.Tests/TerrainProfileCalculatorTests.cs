using MessagePack;

namespace Sufni.Telemetry.Tests;

public class TerrainProfileCalculatorTests
{
    [Fact]
    public void StopOccupiesOneDistanceWindowWithoutAddingWindows()
    {
        var ride = SyntheticRide.Build([new(1000, 10)], new RideRecording(Stop: new(40.5, 100.5)));
        var profile = ride.Profile();
        Assert.Equal(100, profile.Windows.Length);
        var stopped = Assert.Single(profile.Windows.Where(w => !w.Moving));
        Assert.Null(stopped.Roughness);
        Assert.True(stopped.StartSeconds < 41 && stopped.EndSeconds > 100);
    }

    [Fact]
    public void WindowsStayInsideConnectedTrackCoverage()
    {
        var ride = SyntheticRide.Build([new(1000, 10)], new RideRecording(FirstFix: 20, FixLoss: new(40, 70)));
        var windows = ride.Profile().Windows;
        Assert.Equal(20, windows[0].StartSeconds);
        Assert.All(windows, w => Assert.True(w.EndSeconds <= 39 || w.StartSeconds >= 70));
    }

    [Fact]
    public void MissingImuSamplesLeaveWindowMeasurementsAbsent()
    {
        var ride = SyntheticRide.Build([new(300, 10)], new RideRecording(RecordingGap: new(10.2, 10.8)));
        var windows = ride.Profile().Windows;
        Assert.Null(windows[10].Roughness);
        Assert.Null(windows[10].Front);
        Assert.NotNull(windows[9].Frame);
        Assert.NotNull(windows[11].Frame);
    }

    private static readonly RideSection[] CalmMediumRoughCourse = [new(600),
        new(250, LowBandG: 0.9, MiddleBandG: 1, HighBandG: 1.2), new(150, LowBandG: 1.4, MiddleBandG: 1.6, HighBandG: 1.8)];

    private static readonly RideSection[] EveryLevelCourse = [new(200, LowBandG: 0.2, MiddleBandG: 0.3, HighBandG: 0.4),
        .. CalmMediumRoughCourse, new(200, LowBandG: 2, MiddleBandG: 2.2, HighBandG: 2.4)];

    [Theory]
    [InlineData(0, 200, RoughnessClass.Smooth)]
    [InlineData(200, 800, RoughnessClass.Calm)]
    [InlineData(800, 1050, RoughnessClass.Medium)]
    [InlineData(1050, 1200, RoughnessClass.Rough)]
    [InlineData(1200, 1400, RoughnessClass.VeryRough)]
    public void EachGroundSectionGetsTheLevelOfItsBandLevels(double from, double to, RoughnessClass expected)
    {
        var section = SyntheticRide.Build(EveryLevelCourse).Profile().Windows
            .Where(w => w.StartMetres >= from && w.StartMetres < to).ToArray();
        Assert.True(section.Count(w => w.Roughness == expected) >= section.Length * 0.9);
    }

    [Fact]
    public void SummaryRoughShareFollowsTheSectionDistances()
    {
        var summary = TerrainProfileCalculator.Summarize(SyntheticRide.Build(CalmMediumRoughCourse).Profile());
        Assert.InRange(summary.RoughShare, 0.10, 0.20);
    }

    [Fact]
    public void SummaryCountsClimbDistanceApartFromClassifiedGround()
    {
        var summary = TerrainProfileCalculator.Summarize(SyntheticRide.Build([new(600, 7, 8), new(400, 3, -8)]).Profile());
        Assert.InRange(summary.MovingMetres, 990, 1000);
        Assert.InRange(summary.ClimbMetres!.Value, 370, 430);
        Assert.InRange(summary.ClassifiedMetres, 570, 630);
    }

    [Fact]
    public void SummaryHasNoClimbDistanceWithoutGrade()
    {
        var profile = SyntheticRide.Build([new(600, 7, 8), new(400, 3, -8)], new RideRecording(Frame: false)).Profile();
        Assert.Null(TerrainProfileCalculator.Summarize(profile).ClimbMetres);
    }

    [Fact]
    public void SummaryNamesTheSourceThatClassifiedMostDistance()
    {
        var ride = SyntheticRide.Build([new(150), new(300, LowBandG: 2.4, MiddleBandG: 2.2, HighBandG: 2)]);
        Assert.Equal(RoughnessSource.FrameOnly, TerrainProfileCalculator.Summarize(ride.Profile()).DominantSource);
    }

    [Fact]
    public void SpeedNormalisationKeepsGroundClassStableAtDifferentSpeeds()
    {
        var ground = new RideSection(900, LowBandG: 1, MiddleBandG: 1, HighBandG: 1);
        var slow = SyntheticRide.Build([ground with { Speed = 5 }]).Profile().Windows;
        var fast = SyntheticRide.Build([ground with { Speed = 9 }]).Profile().Windows;
        Assert.True(slow.Zip(fast).Count(p => p.First.Roughness == RoughnessClass.Medium &&
            p.Second.Roughness == RoughnessClass.Medium) >= 0.85 * slow.Length);
        Assert.True(slow[20].Frame!.Level < 1.4 && fast[20].Frame!.Level > 1.4);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void FrameSpeedRemovalRestoresTheGroundLevelAtReferenceSpeed(double speed)
    {
        var ground = new RideSection(900, speed, LowBandG: 1, MiddleBandG: 1, HighBandG: 1);
        var groundLevel = Math.Sqrt(3);
        var window = SyntheticRide.Build([ground]).Profile().Windows[20];
        Assert.InRange(window.Frame!.SpeedRemoved, groundLevel * 0.97, groundLevel * 1.03);
    }

    [Theory]
    [InlineData(true, true, 4096, 4096, RoughnessSource.Both)]
    [InlineData(true, false, 4096, 4096, RoughnessSource.FrameOnly)]
    [InlineData(false, true, 4096, 4096, RoughnessSource.LowersOnly)]
    [InlineData(true, true, 4096, 2048, RoughnessSource.FrameOnly)]
    [InlineData(true, true, 2048, 4096, RoughnessSource.LowersOnly)]
    public void ClassificationIdentifiesTheCalibratedSensors(bool frame, bool lowers, int frameScale, int forkScale, RoughnessSource source)
    {
        var profile = SyntheticRide.Build([new(200)], new RideRecording(Frame: frame, Lowers: lowers,
            AccelScale: frameScale, LowersAccelScale: forkScale)).Profile();
        Assert.Equal(source, profile.Windows[10].Source);
    }

    [Fact]
    public void RideWithoutFrameSensorHasNoGrade()
    {
        var profile = SyntheticRide.Build([new(1000)], new RideRecording(Frame: false)).Profile();
        Assert.Equal(TerrainGradeState.NoFrameSensor, profile.GradeState);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(2048)]
    public void SaturatedLowersCannotClassifyWhenBothSensorsRecord(int frameScale)
    {
        var profile = SyntheticRide.Build([new(420, LowBandG: 2.4, MiddleBandG: 2.2, HighBandG: 2)],
            new RideRecording(AccelScale: frameScale, LowersAccelScale: 4096)).Profile();
        var w = profile.Windows[10];
        Assert.True(w.Lowers!.ClippedShare > 0.02);
        if (frameScale == 4096)
        {
            Assert.Equal(RoughnessSource.FrameOnly, w.Source);
            Assert.Equal(RoughnessClass.VeryRough, w.Roughness);
        }
        else Assert.Null(w.Roughness);
    }

    [Fact]
    public void BandLevelsMatchTheGroundBandAmplitudes()
    {
        var frame = SyntheticRide.Build([new(300, LowBandG: 0.3, MiddleBandG: 0.6, HighBandG: 1.2)]).Profile().Windows[15].Frame!;
        Assert.InRange(frame.LowBand, 0.3 * 0.95, 0.3 * 1.05);
        Assert.InRange(frame.MiddleBand, 0.6 * 0.95, 0.6 * 1.05);
        Assert.InRange(frame.HighBand, 1.2 * 0.95, 1.2 * 1.05);
    }

    [Fact]
    public void SuspensionFiguresFollowTheResponseLaw()
    {
        // At 5 m/s a window lasts 2 s, four whole response cycles.
        const double speed = 5;
        var section = new RideSection(300, speed, LowBandG: 0.6, MiddleBandG: 0.8, HighBandG: 0, CompressionGain: 1.5, ReboundGain: 0.5);
        var response = SyntheticRide.ResponseAmplitude(1, speed);
        var front = SyntheticRide.Build([section]).Profile().Windows[15].Front!.Value;
        // Compression takes a quarter of each cycle at 1.5 × the amplitude, rebound three quarters at 0.5 ×.
        var rms = response * Math.Sqrt((0.25 * 1.5 * 1.5 + 0.75 * 0.5 * 0.5) / 2);
        Assert.InRange(front.RmsVelocity, rms * 0.98, rms * 1.02);
        Assert.InRange(front.PeakCompression, 1.5 * response * 0.98, 1.5 * response * 1.02);
        Assert.InRange(front.PeakRebound, 0.5 * response * 0.98, 0.5 * response * 1.02);
    }

    [Fact]
    public void ContinuousSuspensionWithoutSegmentListGetsTheSameFigures()
    {
        var ride = SyntheticRide.Build([new(300, LowBandG: 0.6, MiddleBandG: 0.8)]);
        var unsegmented = ride with { Telemetry = ride.Telemetry with
        {
            Front = ride.Telemetry.Front with { Segments = [] },
            Rear = ride.Telemetry.Rear with { Segments = [] },
        } };
        var expected = ride.Profile().Windows;
        Assert.Equal(expected.Select(w => (w.Front, w.Rear)), unsegmented.Profile().Windows.Select(w => (w.Front, w.Rear)));
        Assert.All(expected.Where(w => w.Moving), w => Assert.NotNull(w.Front));
    }

    [Theory]
    [InlineData(TerrainSensor.Front)]
    [InlineData(TerrainSensor.Rear)]
    [InlineData(TerrainSensor.Frame)]
    [InlineData(TerrainSensor.Lowers)]
    public void SensorReadingZeroInMostMovingWindowsIsIgnored(TerrainSensor sensor)
    {
        var profile = SyntheticRide.Build([new(600)], new RideRecording(Silence: (sensor, new(0, 55)))).Profile();
        Assert.Equal([sensor], profile.SilentSensors);
        Assert.All(profile.Windows, w => Assert.Null(sensor switch
        {
            TerrainSensor.Front => w.Front,
            TerrainSensor.Rear => w.Rear,
            TerrainSensor.Frame => (object?)w.Frame,
            _ => w.Lowers
        }));
    }

    [Fact]
    public void SensorReadingZeroInFewerThanHalfOfMovingWindowsIsKept()
    {
        var profile = SyntheticRide.Build([new(600)], new RideRecording(Silence: (TerrainSensor.Rear, new(0, 30)))).Profile();
        Assert.Empty(profile.SilentSensors);
        Assert.NotNull(profile.Windows[^1].Rear);
    }

    [Fact]
    public void SilentFrameLeavesClassificationToLowersAndRemovesGrade()
    {
        var profile = SyntheticRide.Build([new(1000, GradeDegrees: 8)],
            new RideRecording(Silence: (TerrainSensor.Frame, new(0, 100)))).Profile();
        Assert.All(profile.Windows.Where(w => w.Moving), w => Assert.Equal(RoughnessSource.LowersOnly, w.Source));
        Assert.Empty(profile.FrameMatching);
        Assert.Equal(TerrainGradeState.NoFrameSensor, profile.GradeState);
    }

    [Fact]
    public void MatchingSeriesHoldsTheLogBroadbandLevelOfEachMetre()
    {
        var matching = SyntheticRide.Build([new(300, LowBandG: 0.2, MiddleBandG: 0.6, HighBandG: 0.9)]).Profile().FrameMatching;
        var expected = Math.Log(Math.Sqrt(0.2 * 0.2 + 0.6 * 0.6 + 0.9 * 0.9));
        Assert.All(matching.Where(m => m.Metres is >= 20 and <= 280), m => Assert.InRange(m.LogLevel, expected - 0.03, expected + 0.03));
    }

    [Fact]
    public void UncalibratedSensorStillProvidesPhysicalBandLevelsAndMatchingSeries()
    {
        var profile = SyntheticRide.Build([new(350)], new RideRecording(AccelScale: 2048, Lowers: false)).Profile();
        Assert.Equal(TerrainRoughnessState.ValuesOnly, profile.RoughnessState);
        Assert.Null(profile.Windows[15].Roughness);
        Assert.True(profile.Windows[15].Frame!.MiddleBand > 0);
        Assert.True(profile.FrameMatching.Length >= 340);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void GradeUsesElevationToRemoveMountOffset(double mountPitch)
    {
        var course = new[] { new RideSection(400, 10, 3), new RideSection(400, 10, 8), new RideSection(400, 10, 14) };
        var windows = SyntheticRide.Build(course, new RideRecording(MountPitch: mountPitch)).Profile().Windows;
        Assert.Equal(GradeBand.GentleDescent, windows[20].Band);
        Assert.Equal(GradeBand.Descent, windows[60].Band);
        Assert.Equal(GradeBand.SteepDescent, windows[100].Band);
    }

    private static readonly RideSection[] EveryBandCourse = [new(300, 3, -9), new(300, 3, -4), new(300, 7, 0),
        new(300, 7, 3.5), new(300, 7, 7.5), new(300, 7, 12.5), new(300, 7, 18)];

    [Theory]
    [InlineData(150, GradeBand.SteepClimb)]
    [InlineData(450, GradeBand.Climb)]
    [InlineData(750, GradeBand.Flat)]
    [InlineData(1050, GradeBand.GentleDescent)]
    [InlineData(1350, GradeBand.Descent)]
    [InlineData(1650, GradeBand.SteepDescent)]
    [InlineData(1950, GradeBand.VerySteepDescent)]
    public void GradeGivesASignedBandFromSteepClimbToVerySteepDescent(double metres, GradeBand band)
    {
        var window = SyntheticRide.Build(EveryBandCourse).Profile().Windows.Single(w => w.StartMetres == metres);
        Assert.Equal(band, window.Band);
    }

    // At 3 m/s the frame reads 0.55 of the ground level: 0.23 g on the smooth climb and 0.41 g on the technical one.
    private static readonly RideSection[] SmoothAndTechnicalClimbCourse = [
        new(500, 3, -8, LowBandG: 0.2, MiddleBandG: 0.2, HighBandG: 0.3),
        new(500, 3, -8, LowBandG: 0.4, MiddleBandG: 0.4, HighBandG: 0.5)];

    [Theory]
    [InlineData(0, 500, RoughnessClass.SmoothClimb)]
    [InlineData(500, 1000, RoughnessClass.TechnicalClimb)]
    public void ClimbsGetAClimbLevelFromRawFrameVibration(double from, double to, RoughnessClass expected)
    {
        var section = SyntheticRide.Build(SmoothAndTechnicalClimbCourse).Profile().Windows
            .Where(w => w.StartMetres >= from + 50 && w.StartMetres < to - 50).ToArray();
        Assert.All(section, w => { Assert.Equal(expected, w.Roughness); Assert.Null(w.Source); });
    }

    [Fact]
    public void GradeRemovesForwardBrakingAcceleration()
    {
        var ride = SyntheticRide.Build([new(1600, 22, 3)], new RideRecording(FixRate: 3, Braking: new(12, 18)));
        var window = ride.Profile().Windows.First(w => w.StartSeconds >= 14);
        Assert.Equal(GradeBand.GentleDescent, window.Band);
        Assert.InRange(window.Grade!.Value, 0.025, 0.085);
    }

    [Fact]
    public void ShortRideKeepsRoughnessWithoutGrades()
    {
        var profile = SyntheticRide.Build([new(500)]).Profile();
        Assert.Equal(TerrainGradeState.TooShort, profile.GradeState);
        Assert.All(profile.Windows, w => { Assert.NotNull(w.Roughness); Assert.Null(w.Grade); });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingMovingEndpointElevationDisablesGradeOnly(bool first)
    {
        var ride = SyntheticRide.Build([new(1000, 10, 14)]);
        var fixes = ride.Fixes.ToBuilder();
        var index = first ? 0 : fixes.Count - 1;
        fixes[index] = fixes[index] with { Elevation = null };
        var profile = (ride with { Fixes = fixes.ToImmutable() }).Profile();
        Assert.Equal(TerrainGradeState.NoElevation, profile.GradeState);
        Assert.All(profile.Windows, w => { Assert.Null(w.Grade); Assert.NotNull(w.Roughness); });
    }

    [Fact]
    public void ClimbingAtWalkingPaceCountsAsMoving()
    {
        var windows = SyntheticRide.Build([new(100, 1.5, -6)]).Profile().Windows;
        Assert.All(windows, w => Assert.True(w.Moving));
    }

    [Fact]
    public void RideBelowMovingSpeedIsUnavailableForNoMovement()
    {
        var ride = SyntheticRide.Build([new(100, 0.8)]);
        Assert.Equal(new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoMovement),
            TerrainProfileCalculator.Calculate(ride.Telemetry, ride.Fixes));
    }

    [Fact]
    public void TrackOutsideTheSessionIsUnavailableForNoTrack()
    {
        var ride = SyntheticRide.Build([new(100)]);
        Assert.Equal(new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoTrack),
            TerrainProfileCalculator.Calculate(ride.Telemetry, ride.Fixes.Select(f => f with { Seconds = f.Seconds + 100 }).ToArray()));
    }

    [Fact]
    public void SerializedProfilePreservesClassifiedWindowsSensorMatchingAndSilentSensors()
    {
        var profile = SyntheticRide.Build([new(210)], new RideRecording(Silence: (TerrainSensor.Rear, new(0, 25)))).Profile();
        var restored = MessagePackSerializer.Deserialize<TerrainProfile>(MessagePackSerializer.Serialize(profile));
        Assert.Equal(profile.Windows.ToArray(), restored.Windows.ToArray());
        Assert.Equal(profile.FrameMatching.ToArray(), restored.FrameMatching.ToArray());
        Assert.Equal([TerrainSensor.Rear], restored.SilentSensors);
    }
}
