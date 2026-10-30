using Sufni.Kinematics;
using Sufni.App.Shared.Models;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Services;

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
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 24,
                Resolution = 4,
                Type = SensorType.LinearShockStroke,
            })
        };

        var result = TryBuild(setup, bike);

        var calibration = Assert.IsType<RearTravelCalibrationResult.Built>(result).Calibration;
        Assert.Equal(50, calibration!.MaxTravel, 6);
        Assert.False(calibration.MeasurementWraps);
        Assert.Equal(12, calibration.MeasurementToTravel(3), 6);
        Assert.Equal(20, calibration.MeasurementToTravel(5), 6);
        Assert.Equal(calibration.MaxTravel, calibration.MeasurementToTravel(15), 6);
    }

    [Fact]
    public void TryBuild_ReturnsConfigurationFailure_WhenLeverageRatioShockStrokeDoesNotMatchCurveMax()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: 15));
        var setup = new Setup(Guid.NewGuid(), "curve setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 24,
                Resolution = 4,
                Type = SensorType.LinearShockStroke,
            })
        };

        var result = TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Configuration, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    [Fact]
    public void TryBuild_ReturnsConfigurationFailure_WhenLeverageRatioShockStrokeIsMissing()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: null));
        var setup = new Setup(Guid.NewGuid(), "curve setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 24,
                Resolution = 4,
                Type = SensorType.LinearShockStroke,
            })
        };

        var result = TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Configuration, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    [Fact]
    public void TryBuild_AllowsSmallLeverageRatioShockStrokeRoundingDifference()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (9.99999935, 25), (19.9999987, 50)),
            shockStroke: 20));
        var setup = new Setup(Guid.NewGuid(), "curve setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 24,
                Resolution = 4,
                Type = SensorType.LinearShockStroke,
            })
        };

        var result = TryBuild(setup, bike);

        var calibration = Assert.IsType<RearTravelCalibrationResult.Built>(result).Calibration;
        Assert.Equal(50, calibration!.MaxTravel, 6);
    }

    [Fact]
    public void TryBuild_ComposesLinkageRearTravelCalibration()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var bike = new Bike(Guid.NewGuid(), "linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
        };
        var setup = new Setup(Guid.NewGuid(), "linkage setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = linkage.ShockStroke,
                Resolution = 4,
            })
        };
        var kinematicSolutionCache = new TrackingKinematicSolutionCache();

        var result = TryBuild(setup, bike, kinematicSolutionCache);

        var calibration = Assert.IsType<RearTravelCalibrationResult.Built>(result).Calibration;
        Assert.Equal(0, calibration!.MeasurementToTravel(0), 6);
        Assert.False(calibration.MeasurementWraps);
        Assert.True(calibration.MeasurementToTravel(5) > 0);
        Assert.Equal(calibration.MaxTravel, calibration.MeasurementToTravel(15), 6);
        Assert.Equal(1, kinematicSolutionCache.GetOrSolveCount);
    }

    [Fact]
    public void TryBuild_ReturnsNotApplicable_ForHardtail()
    {
        var bike = new Bike(Guid.NewGuid(), "hardtail")
        {
            RearSuspension = new RearSuspensionSpec.Hardtail(),
        };
        var setup = new Setup(Guid.NewGuid(), "hardtail setup")
        {
            BikeId = bike.Id,
        };

        var result = TryBuild(setup, bike);

        Assert.IsType<RearTravelCalibrationResult.NotApplicable>(result);
    }

    public static TheoryData<RearSuspensionSpec> DraftRearSuspensions => new()
    {
        new RearSuspensionSpec.LinkageDraft(),
        new RearSuspensionSpec.LeverageRatioDraft(),
    };

    [Theory]
    [MemberData(nameof(DraftRearSuspensions))]
    public void TryBuild_ReturnsConfigurationFailure_WhenRearSuspensionStateIsDraft(RearSuspensionSpec rearSuspension)
    {
        var bike = new Bike(Guid.NewGuid(), "invalid bike")
        {
            RearSuspension = rearSuspension,
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

        var result = TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Configuration, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    [Fact]
    public void TryBuild_ReturnsNotApplicable_WhenRearSensorConfigurationIsMissing()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50))));
        var setup = new Setup(Guid.NewGuid(), "missing rear config")
        {
            BikeId = bike.Id,
        };

        var result = TryBuild(setup, bike);

        Assert.IsType<RearTravelCalibrationResult.NotApplicable>(result);
    }

    [Fact]
    public void TryBuild_ComposesRotationalShockAndLinkageMapping()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var bike = new Bike(Guid.NewGuid(), "rotational linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
        };
        var mapping = new JointNameMapping();
        var setup = new Setup(Guid.NewGuid(), "rotational setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new RotationalShockSensorConfiguration
            {
                CentralJoint = mapping.RearWheel,
                AdjacentJoint1 = mapping.BottomBracket,
                AdjacentJoint2 = mapping.ShockEye1,
            })
        };
        var kinematicSolutionCache = new TrackingKinematicSolutionCache();

        var result = TryBuild(setup, bike, kinematicSolutionCache);

        var calibration = Assert.IsType<RearTravelCalibrationResult.Built>(result).Calibration;
        Assert.True(calibration!.MeasurementWraps);
        Assert.True(calibration!.MaxTravel > 0);
        Assert.True(double.IsFinite(calibration.MeasurementToTravel(0)));
        Assert.True(double.IsFinite(calibration.MeasurementToTravel(128)));
        Assert.NotEqual(calibration.MeasurementToTravel(0), calibration.MeasurementToTravel(128));
        Assert.Equal(1, kinematicSolutionCache.GetOrSolveCount);
    }

    [Fact]
    public void TryBuild_ReturnsConfigurationFailure_ForIncompatibleSensorAndBikePair()
    {
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50))));
        var setup = new Setup(Guid.NewGuid(), "incompatible setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 0.3,
                Resolution = 4,
                Type = SensorType.LinearShock,
            })
        };

        var result = TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Configuration, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    [Theory]
    [InlineData(1601)]
    [InlineData(1602)]
    public void TryBuild_PreservesCalibration_AcrossLookupCurveSizeBoundary(int pointCount)
    {
        var points = Enumerable.Range(0, pointCount)
            .Select(index =>
            {
                var fraction = index / (double)(pointCount - 1);
                return new LeverageRatioPoint(60 * fraction, 160 * fraction);
            })
            .ToArray();
        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            LeverageRatioSpec.FromPoints(points),
            shockStroke: 60));
        var setup = new Setup(Guid.NewGuid(), "curve admission setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 60,
                Resolution = 12,
                Type = SensorType.LinearShockStroke,
            })
        };

        var result = TryBuild(setup, bike);

        var calibration = Assert.IsType<RearTravelCalibrationResult.Built>(result).Calibration;
        Assert.Equal(160, calibration.MeasurementToTravel(4095), 6);
        Assert.Equal(80, calibration.MeasurementToTravel(2048), 1);
    }

    [Fact]
    public void TryBuild_ReturnsConfigurationFailure_WhenRotationalSensorGeometryIsDegenerate()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var bike = new Bike(Guid.NewGuid(), "degenerate rotational bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
        };
        var mapping = new JointNameMapping();
        var setup = new Setup(Guid.NewGuid(), "degenerate rotational setup")
        {
            BikeId = bike.Id,
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new RotationalShockSensorConfiguration
            {
                CentralJoint = mapping.RearWheel,
                AdjacentJoint1 = mapping.RearWheel,
                AdjacentJoint2 = mapping.ShockEye1,
            })
        };

        var result = TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Configuration, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    [Fact]
    public void TryBuild_ReturnsConfigurationFailure_ForMalformedSensorJson()
    {
        var bike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)), shockStroke: 20);
        var setup = TestSnapshots.Setup(bikeId: bike.Id) with { RearSensorConfigurationJson = "{broken" };
        var builder = new RearTravelCalibrationBuilder(new KinematicSolutionCache());

        var result = builder.TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Configuration, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    [Fact]
    public void TryBuild_ReturnsCalculationFailure_WhenSolverFailsUnexpectedly()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bike = TestSnapshots.Bike() with { RearSuspension = new RearSuspensionSpec.Linkage(linkage), ShockStroke = linkage.ShockStroke };
        var setup = TestSnapshots.Setup(bikeId: bike.Id) with
        {
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = linkage.ShockStroke,
                Resolution = 12
            })
        };
        var builder = new RearTravelCalibrationBuilder(new TrackingKinematicSolutionCache { Failure = new IOException("solver failed") });

        var result = builder.TryBuild(setup, bike);

        Assert.Equal(RearCalibrationFailureReason.Calculation, Assert.IsType<RearTravelCalibrationResult.Failed>(result).Reason);
    }

    private static RearTravelCalibrationResult TryBuild(
        Setup setup,
        Bike bike,
        IKinematicSolutionCache? kinematicSolutionCache = null)
    {
        var builder = new RearTravelCalibrationBuilder(kinematicSolutionCache ?? new KinematicSolutionCache());
        return builder.TryBuild(SetupSnapshot.From(setup, boardId: null), BikeSnapshot.From(bike));
    }

    private sealed class TrackingKinematicSolutionCache : IKinematicSolutionCache
    {
        public int GetOrSolveCount { get; private set; }
        public Exception? Failure { get; init; }

        public KinematicSolution GetOrSolve(LinkageSpec linkage, int steps = 200, int iterations = 1000)
        {
            GetOrSolveCount++;
            if (Failure is { } failure) throw failure;
            return new KinematicSolver(linkage, steps, iterations).SolveSuspensionMotion();
        }
    }
}
