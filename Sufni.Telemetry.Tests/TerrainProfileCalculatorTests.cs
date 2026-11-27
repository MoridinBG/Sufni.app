using System.Collections.Immutable;
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
        Assert.Null(windows[10].Frame);
        Assert.Null(windows[10].Front);
        Assert.NotNull(windows[9].Frame);
        Assert.NotNull(windows[11].Frame);
    }

    [Fact]
    public void SummaryCountsMovingAndClimbDistance()
    {
        var summary = TerrainProfileCalculator.Summarize(SyntheticRide.Build([new(600, 7, 8), new(400, 3, -8)]).Profile());
        Assert.InRange(summary.MovingMetres, 990, 1000);
        Assert.InRange(summary.ClimbMetres!.Value, 370, 430);
    }

    [Fact]
    public void SummaryHasNoClimbDistanceWithoutGrade()
    {
        var profile = SyntheticRide.Build([new(600, 7, 8), new(400, 3, -8)], new RideRecording(Frame: false)).Profile();
        Assert.Null(TerrainProfileCalculator.Summarize(profile).ClimbMetres);
    }

    [Fact]
    public void SummaryDistributesTheEligibleDistanceOfEachSensorOverLevelBins()
    {
        var climb = TerrainProfiles.Window(3) with { Band = GradeBand.Climb, Frame = Reading(level: 7, speedRemoved: 4) };
        var profile = TerrainProfiles.Profile(
        [
            TerrainProfiles.Window(0) with { Frame = Reading(level: 9, speedRemoved: 3), Fork = Reading(10), RearAxle = Reading(-5) },
            TerrainProfiles.Window(1) with { Frame = Reading(level: 9, speedRemoved: 3) },
            TerrainProfiles.Window(2) with { Frame = Reading(3, clippedShare: 0.05), Fork = Reading(10) with { Level = 0 } },
            climb,
            TerrainProfiles.Window(4, moving: false) with { Frame = Reading(3) }
        ]);
        var summary = TerrainProfileCalculator.Summarize(profile);
        Assert.Equal(Distribution((3, 20)), summary.FrameImu);
        Assert.Equal(Distribution((10, 10)), summary.ForkImu);
        Assert.Equal(Distribution((-5, 10)), summary.RearAxleImu);
        Assert.Equal(Distribution((7, 10)), summary.FrameClimb);
    }

    [Fact]
    public void SummaryHasNoDistributionForASensorWithoutAnEligibleReading()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Fork = Reading(4, clippedShare: 0.05) }]);
        var summary = TerrainProfileCalculator.Summarize(profile);
        Assert.Null(summary.ForkImu);
        Assert.Null(summary.FrameImu);
    }

    [Fact]
    public void SummariesWithEqualDistributionsAreEqual()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Frame = Reading(2), RearAxle = Reading(8) }]);
        Assert.Equal(TerrainProfileCalculator.Summarize(profile), TerrainProfileCalculator.Summarize(profile));
        Assert.NotEqual(Distribution((2, 10)), Distribution((2, 20)));
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
    [InlineData(5)]
    [InlineData(9)]
    public void ForkSpeedRemovalRestoresItsGroundLevelAtReferenceSpeed(double speed)
    {
        var ground = new RideSection(900, speed, LowBandG: 1, MiddleBandG: 1, HighBandG: 1);
        var groundLevel = SyntheticRide.ForkLevelFactor * Math.Sqrt(3);
        var window = SyntheticRide.Build([ground]).Profile().Windows[20];
        Assert.InRange(window.Fork!.SpeedRemoved, groundLevel * 0.97, groundLevel * 1.03);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void RearAxleSpeedRemovalRestoresItsGroundLevelAtReferenceSpeed(double speed)
    {
        var ground = new RideSection(900, speed, LowBandG: 1, MiddleBandG: 1, HighBandG: 1);
        var groundLevel = SyntheticRide.RearAxleLevelFactor * Math.Sqrt(3);
        var window = SyntheticRide.Build([ground], new RideRecording(RearAxle: true)).Profile().Windows[20];
        Assert.InRange(window.RearAxle!.SpeedRemoved, groundLevel * 0.97, groundLevel * 1.03);
    }

    [Fact]
    public void RideWithoutFrameSensorHasNoGrade()
    {
        var profile = SyntheticRide.Build([new(1000)], new RideRecording(Frame: false)).Profile();
        Assert.Equal(TerrainGradeState.NoFrameSensor, profile.GradeState);
    }

    [Fact]
    public void SaturatedForkReportsItsClippedShare()
    {
        var profile = SyntheticRide.Build([new(420, LowBandG: 2.4, MiddleBandG: 2.2, HighBandG: 2)]).Profile();
        Assert.True(profile.Windows[10].Fork!.ClippedShare > TerrainProfileCalculator.ClippedShareLimit);
        Assert.True(profile.Windows[10].Frame!.ClippedShare <= TerrainProfileCalculator.ClippedShareLimit);
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
    public void TravelFiguresFollowTheStrokeShape()
    {
        const double speed = 5;
        var section = new RideSection(300, speed, LowBandG: 0.6, MiddleBandG: 0.8, HighBandG: 0);
        // Equal gains split each cycle in half, so travel swings amplitude / 2π above its 35 mm floor.
        var swing = SyntheticRide.ResponseAmplitude(1, speed) / (2 * Math.PI);
        var front = SyntheticRide.Build([section]).Profile().Windows[15].Front!.Value;
        Assert.InRange(front.MeanTravel, 35 + swing / 2 - 0.3, 35 + swing / 2 + 0.3);
        Assert.InRange(front.MedianStrokeSize!.Value, swing * 0.95, swing * 1.05);
    }

    // Two strokes per crank turn: 2.8 Hz is 84 rpm. Little ground input leaves the rear moving mostly at the pedal rhythm.
    private static readonly RideSection PedalledClimb = new(300, 3, -6, LowBandG: 0.03, MiddleBandG: 0.04, HighBandG: 0.05,
        PedalHz: 2.8, BobMillimetres: 8);

    private static PedallingFigures? PedallingAt150Metres(RideSection section, RideRecording? recording = null) =>
        SyntheticRide.Build([section], recording).Profile().Windows.Single(w => w.StartMetres == 150).Pedalling;

    [Fact]
    public void PedalledClimbGivesCadenceAndRearBob()
    {
        var pedalling = PedallingAt150Metres(PedalledClimb)!.Value;
        Assert.True(pedalling.Detected);
        Assert.InRange(pedalling.CadenceRpm!.Value, 81, 87);
        Assert.InRange(pedalling.BobMillimetres!.Value, 7.2, 8.8);
    }

    [Fact]
    public void RearRhythmWithoutFrameRhythmIsNotPedalling()
    {
        Assert.Equal(new PedallingFigures(false, null, null), PedallingAt150Metres(PedalledClimb with { PedalHz = 0, BobMillimetres = 0 }));
    }

    [Fact]
    public void WithoutFrameSensorTheRearRhythmAloneDetectsPedalling()
    {
        Assert.True(PedallingAt150Metres(PedalledClimb, new RideRecording(Frame: false))!.Value.Detected);
    }

    [Fact]
    public void SilentRearSensorLeavesPedallingUnknown()
    {
        Assert.Null(PedallingAt150Metres(PedalledClimb, new RideRecording(Silence: (TerrainSensor.ShockTravel, new(0, 100)))));
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
    [InlineData(TerrainSensor.ForkTravel)]
    [InlineData(TerrainSensor.ShockTravel)]
    [InlineData(TerrainSensor.FrameImu)]
    [InlineData(TerrainSensor.ForkImu)]
    [InlineData(TerrainSensor.RearAxleImu)]
    public void SensorReadingZeroInMostMovingWindowsIsIgnored(TerrainSensor sensor)
    {
        var profile = SyntheticRide.Build([new(600)], new RideRecording(RearAxle: true, Silence: (sensor, new(0, 55)))).Profile();
        Assert.Equal([sensor], profile.SilentSensors);
        Assert.All(profile.Windows, w => Assert.Null(sensor switch
        {
            TerrainSensor.ForkTravel => w.Front,
            TerrainSensor.ShockTravel => w.Rear,
            TerrainSensor.FrameImu => (object?)w.Frame,
            TerrainSensor.ForkImu => w.Fork,
            _ => w.RearAxle
        }));
    }

    [Fact]
    public void SensorReadingZeroInFewerThanHalfOfMovingWindowsIsKept()
    {
        var profile = SyntheticRide.Build([new(600)], new RideRecording(Silence: (TerrainSensor.ShockTravel, new(0, 30)))).Profile();
        Assert.Empty(profile.SilentSensors);
        Assert.NotNull(profile.Windows[^1].Rear);
    }

    [Fact]
    public void SilentFrameKeepsTheForkReadingsAndRemovesGrade()
    {
        var profile = SyntheticRide.Build([new(1000, GradeDegrees: 8)],
            new RideRecording(Silence: (TerrainSensor.FrameImu, new(0, 100)))).Profile();
        Assert.All(profile.Windows.Where(w => w.Moving), w => Assert.NotNull(w.Fork));
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
    public void SensorAtAnotherAccelerometerScaleProvidesBandLevelsAndMatchingSeries()
    {
        var profile = SyntheticRide.Build([new(350)], new RideRecording(AccelScale: 2048, Fork: false)).Profile();
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

    [Theory]
    [InlineData(1.5, GradeBand.GentleDescent)]
    [InlineData(0.5, GradeBand.Flat)]
    public void GradeBandChangesOnlyWhenTheGradePassesAnEdgeByOneDegree(double middleDegrees, GradeBand middle)
    {
        var windows = SyntheticRide.Build([new(300, 7, 3), new(300, 7, middleDegrees), new(300, 7, 3)]).Profile().Windows;
        Assert.Equal(GradeBand.GentleDescent, windows.Single(w => w.StartMetres == 150).Band);
        Assert.Equal(middle, windows.Single(w => w.StartMetres == 450).Band);
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
    public void RoughnessPiecesEndWhereTheGroundChangesAndReadEachSideWhole()
    {
        var pieces = SyntheticRide.Build([new(300), new(300, LowBandG: 1.2, MiddleBandG: 1.8, HighBandG: 2.1)]).Profile().RoughnessPieces;
        var before = pieces.Last(p => p.StartMetres < 299);
        var after = pieces.First(p => p.StartMetres >= 299);
        Assert.InRange(after.StartMetres, 299, 302);
        Assert.InRange(after.Frame!.SpeedRemoved / before.Frame!.SpeedRemoved, 2.7, 3.3);
    }

    [Fact]
    public void GradePiecesEndWhereTheGradeChangesAndGiveTheWindowsTheirBand()
    {
        var profile = SyntheticRide.Build([new(500, 7, 0), new(500, 7, 8)]).Profile();
        var change = profile.GradePieces.First(p => p.Band == GradeBand.Descent);
        Assert.InRange(change.StartMetres, 490, 510);
        Assert.All(profile.Windows.Where(w => w.StartMetres + 10 <= change.StartMetres), w => Assert.Equal(GradeBand.Flat, w.Band));
        Assert.All(profile.Windows.Where(w => w.StartMetres >= change.StartMetres), w => Assert.Equal(GradeBand.Descent, w.Band));
    }

    [Fact]
    public void ShortRideKeepsReadingsWithoutGrades()
    {
        var profile = SyntheticRide.Build([new(500)]).Profile();
        Assert.Equal(TerrainGradeState.TooShort, profile.GradeState);
        Assert.All(profile.Windows, w => { Assert.NotNull(w.Frame); Assert.Null(w.Grade); });
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
        Assert.All(profile.Windows, w => { Assert.Null(w.Grade); Assert.NotNull(w.Frame); });
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
    public void SerializedProfilePreservesWindowsPiecesSensorMatchingAndSilentSensors()
    {
        var profile = SyntheticRide.Build([new(210)], new RideRecording(Silence: (TerrainSensor.ShockTravel, new(0, 25)))).Profile();
        var restored = MessagePackSerializer.Deserialize<TerrainProfile>(MessagePackSerializer.Serialize(profile));
        Assert.Equal(profile.Windows.ToArray(), restored.Windows.ToArray());
        Assert.Equal(profile.FrameMatching.ToArray(), restored.FrameMatching.ToArray());
        Assert.Equal(profile.RoughnessPieces.ToArray(), restored.RoughnessPieces.ToArray());
        Assert.Equal(profile.GradePieces.ToArray(), restored.GradePieces.ToArray());
        Assert.Equal([TerrainSensor.ShockTravel], restored.SilentSensors);
    }

    /// <summary>A reading whose level lies in the middle of level bin <paramref name="bin"/>.</summary>
    private static SensorRoughness Reading(int bin, double clippedShare = 0) =>
        TerrainProfiles.Sensor(BinCentre(bin), clippedShare: clippedShare);

    /// <summary>A reading whose raw and speed-removed levels lie in two different bins.</summary>
    private static SensorRoughness Reading(int level, int speedRemoved) =>
        TerrainProfiles.Sensor(BinCentre(level)) with { SpeedRemoved = BinCentre(speedRemoved) };

    // Twenty bins span one 1.5× class step.
    private static double BinCentre(int bin) => Math.Pow(1.5, (bin + 0.5) / 20);

    private static LevelDistribution Distribution(params (int Bin, double Metres)[] bins) =>
        new(bins.ToImmutableSortedDictionary(b => b.Bin, b => b.Metres));
}
