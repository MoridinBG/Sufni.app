using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Tests.Infrastructure;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Models.SensorConfigurations;

public class RearShockCalibrationFactoryTests
{
    [Fact]
    public void TryBuild_ReturnsLinearShockCalibration_ForLinkageBike()
    {
        var bike = CreateLinkageBike(shockStroke: 0.5);
        var payload = new LinearShockSensorConfiguration
        {
            Length = 0.3,
            Resolution = 4,
        };

        var success = RearShockCalibrationFactory.TryBuild(
            SensorConfiguration.ToJson(payload),
            bike,
            out var calibration,
            out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(calibration);
        Assert.Equal(0.5, calibration!.MaxShockStroke, 6);
        Assert.Equal(0.25, calibration.MeasurementToShockStroke(5), 6);
    }

    [Fact]
    public void TryBuild_ReturnsLinearShockStrokeCalibration_ForLeverageRatioBike()
    {
        var bike = CreateLeverageRatioBike(shockStroke: 20);
        var payload = new LinearShockStrokeSensorConfiguration
        {
            Length = 24,
            Resolution = 4,
        };

        var success = RearShockCalibrationFactory.TryBuild(
            SensorConfiguration.ToJson(payload),
            bike,
            out var calibration,
            out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(calibration);
        Assert.Equal(20, calibration!.MaxShockStroke, 6);
        Assert.Equal(12, calibration.MeasurementToShockStroke(3), 6);
    }

    [Fact]
    public void TryBuild_ReturnsRotationalShockCalibration_ForLinkageBike()
    {
        var bike = CreateLinkageBike(shockStroke: 0.5);
        var mapping = new JointNameMapping();
        var payload = new RotationalShockSensorConfiguration
        {
            CentralJoint = mapping.RearWheel,
            AdjacentJoint1 = mapping.BottomBracket,
            AdjacentJoint2 = mapping.ShockEye1,
        };

        var success = RearShockCalibrationFactory.TryBuild(
            SensorConfiguration.ToJson(payload),
            bike,
            out var calibration,
            out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(calibration);
        Assert.True(calibration!.MaxShockStroke > 0);
        Assert.True(double.IsFinite(calibration.MeasurementToShockStroke(0)));
        Assert.True(double.IsFinite(calibration.MeasurementToShockStroke(128)));
        Assert.NotEqual(calibration.MeasurementToShockStroke(0), calibration.MeasurementToShockStroke(128));
    }

    [Fact]
    public void TryBuild_ReturnsFalse_ForIncompatibleSensorAndBikePair()
    {
        var bike = CreateLeverageRatioBike();
        var payload = new LinearShockSensorConfiguration
        {
            Length = 0.3,
            Resolution = 4,
        };

        var success = RearShockCalibrationFactory.TryBuild(
            SensorConfiguration.ToJson(payload),
            bike,
            out var calibration,
            out var errorMessage);

        Assert.False(success);
        Assert.Null(calibration);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    private static Bike CreateLinkageBike(double shockStroke)
    {
        var linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        linkage.ShockStroke = shockStroke;

        return new Bike(Guid.NewGuid(), "linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = shockStroke,
            RearSuspensionKind = RearSuspensionKind.Linkage,
            Linkage = linkage,
        };
    }

    private static Bike CreateLeverageRatioBike(double shockStroke = 20)
    {
        return Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: shockStroke));
    }
}