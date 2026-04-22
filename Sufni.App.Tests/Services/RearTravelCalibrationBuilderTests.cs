using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Services;

public class RearTravelCalibrationBuilderTests
{
    [Fact]
    public void TryBuild_ComposesLinearShockStrokeAndLeverageRatioMapping()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: 20));
        var setup = new Setup(Guid.NewGuid(), "curve setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockStrokeSensorConfiguration
            {
                Length = 24,
                Resolution = 4,
            })
        };

        var success = RearTravelCalibrationBuilder.TryBuild(setup, bike, out var calibration, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(calibration);
        Assert.Equal(50, calibration!.MaxTravel, 6);
        Assert.Equal(30, calibration.MeasurementToTravel(3), 6);
        Assert.Equal(50, calibration.MeasurementToTravel(5), 6);
    }

    [Fact]
    public void TryBuild_ComposesLinkageRearTravelCalibration()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var bike = new Bike(Guid.NewGuid(), "linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = linkage.ShockStroke,
            RearSuspensionKind = RearSuspensionKind.Linkage,
            Linkage = linkage,
        };
        var setup = new Setup(Guid.NewGuid(), "linkage setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 0.3,
                Resolution = 4,
            })
        };

        var success = RearTravelCalibrationBuilder.TryBuild(setup, bike, out var calibration, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(calibration);
        Assert.Equal(0, calibration!.MeasurementToTravel(0), 6);
        Assert.True(calibration.MeasurementToTravel(5) > 0);
        Assert.Equal(calibration.MaxTravel, calibration.MeasurementToTravel(10), 6);
    }

    [Fact]
    public void TryBuild_ReturnsFalse_WhenRearSuspensionStateIsInvalid()
    {
        var bike = new Bike(Guid.NewGuid(), "invalid bike")
        {
            RearSuspensionKind = RearSuspensionKind.Linkage,
            ShockStroke = 0.5,
        };
        var setup = new Setup(Guid.NewGuid(), "invalid setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 0.3,
                Resolution = 4,
            })
        };

        var success = RearTravelCalibrationBuilder.TryBuild(setup, bike, out var calibration, out var errorMessage);

        Assert.False(success);
        Assert.Null(calibration);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    [Fact]
    public void TryBuild_ReturnsFalse_WhenRearSensorConfigurationIsMissing()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50))));
        var setup = new Setup(Guid.NewGuid(), "missing rear config")
        {
            BikeId = bike.Id,
        };

        var success = RearTravelCalibrationBuilder.TryBuild(setup, bike, out var calibration, out var errorMessage);

        Assert.False(success);
        Assert.Null(calibration);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }
}