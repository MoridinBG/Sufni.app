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

    [Fact]
    public void EachGroundSectionGetsItsDescriptionIndependentOfSuspensionResponse()
    {
        RideSection[] course = [new(600), new(250, LowBandG: 0.9, MiddleBandG: 1, HighBandG: 1.2),
            new(150, LowBandG: 1.4, MiddleBandG: 1.6, HighBandG: 1.8, CompressionGain: 0.3)];
        var profile = SyntheticRide.Build(course).Profile();
        var windows = profile.Windows;
        void Check(double from, double to, RoughnessClass expected)
        {
            var section = windows.Where(w => w.StartMetres >= from && w.StartMetres < to).ToArray();
            Assert.True(section.Count(w => w.Roughness == expected) >= section.Length * 0.9);
        }
        Check(0, 600, RoughnessClass.Calm);
        Check(600, 850, RoughnessClass.Medium);
        Check(850, 1000, RoughnessClass.Rough);
        var summary = TerrainProfileCalculator.Summarize(profile);
        Assert.InRange(summary.CalmShare, 0.55, 0.65);
        Assert.InRange(summary.RoughShare, 0.10, 0.20);
        Assert.Equal(RoughnessSource.Both, summary.DominantSource);
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
        if (!frame) Assert.Equal(TerrainSteepnessState.NoFrameSensor, profile.SteepnessState);
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
            Assert.Equal(RoughnessClass.Rough, w.Roughness);
        }
        else Assert.Null(w.Roughness);
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
        Assert.Equal(SteepnessClass.Gentle, windows[20].Steepness);
        Assert.Equal(SteepnessClass.Moderate, windows[60].Steepness);
        Assert.Equal(SteepnessClass.Steep, windows[100].Steepness);
    }

    [Fact]
    public void GradeRemovesForwardBrakingAcceleration()
    {
        var ride = SyntheticRide.Build([new(1600, 22, 3)], new RideRecording(FixRate: 3, Braking: new(12, 18)));
        var window = ride.Profile().Windows.First(w => w.StartSeconds >= 14);
        Assert.Equal(SteepnessClass.Gentle, window.Steepness);
        Assert.InRange(window.Grade!.Value, 0.025, 0.085);
    }

    [Fact]
    public void ShortRideKeepsRoughnessWithoutGrades()
    {
        var profile = SyntheticRide.Build([new(500)]).Profile();
        Assert.Equal(TerrainSteepnessState.TooShort, profile.SteepnessState);
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
        Assert.Equal(TerrainSteepnessState.NoElevation, profile.SteepnessState);
        Assert.All(profile.Windows, w => { Assert.Null(w.Grade); Assert.NotNull(w.Roughness); });
    }

    [Fact]
    public void UnavailableResultDistinguishesMissingCoverageFromInsufficientMovement()
    {
        var ride = SyntheticRide.Build([new(100, 2)]);
        Assert.Equal(new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoMovement),
            TerrainProfileCalculator.Calculate(ride.Telemetry, ride.Fixes));
        Assert.Equal(new TerrainProfileResult.Unavailable(TerrainUnavailableReason.NoTrack),
            TerrainProfileCalculator.Calculate(ride.Telemetry, ride.Fixes.Select(f => f with { Seconds = f.Seconds + 100 }).ToArray()));
    }

    [Fact]
    public void SerializedProfilePreservesClassifiedWindowsAndSensorMatching()
    {
        var profile = SyntheticRide.Build([new(210)]).Profile();
        var restored = MessagePackSerializer.Deserialize<TerrainProfile>(MessagePackSerializer.Serialize(profile));
        Assert.Equal(profile.Windows.ToArray(), restored.Windows.ToArray());
        Assert.Equal(profile.FrameMatching.ToArray(), restored.FrameMatching.ToArray());
    }
}
