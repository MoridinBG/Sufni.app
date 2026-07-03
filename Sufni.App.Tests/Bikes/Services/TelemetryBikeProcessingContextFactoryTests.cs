using Sufni.Kinematics;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Bikes.Services;

public class TelemetryBikeProcessingContextFactoryTests
{
    [Fact]
    public void Create_ReturnsSameContext_ForIdenticalProcessingInputs()
    {
        var factory = CreateFactory(out var rearCalibrationBuilder);
        var bike = CreateBike();
        var setup = CreateSetup(
            bike.Id,
            frontSensorConfigurationJson: LinearForkJson());

        var first = factory.Create(setup, bike);
        var second = factory.Create(setup, bike);

        Assert.Same(first, second);
        Assert.Equal(1, rearCalibrationBuilder.Calls);
        Assert.NotNull(first.FrontSensorConfiguration);
        Assert.NotNull(first.BikeData.FrontMeasurementToTravel);
        Assert.Equal(first.FrontSensorConfiguration!.MaxTravel, first.BikeData.FrontMaxTravel);
    }

    [Fact]
    public void Create_IgnoresBikeImageAndUpdatedFields()
    {
        var factory = CreateFactory(out var rearCalibrationBuilder);
        var bike = CreateBike(updated: 1);
        var setup = CreateSetup(bike.Id);
        var changedBikeMetadata = bike with
        {
            ImageBytes = [1, 2, 3],
            Updated = 999,
        };

        var first = factory.Create(setup, bike);
        var second = factory.Create(setup, changedBikeMetadata);

        Assert.Same(first, second);
        Assert.Equal(1, rearCalibrationBuilder.Calls);
        Assert.Empty(rearCalibrationBuilder.LastBike?.ImageBytes ?? []);
    }

    [Fact]
    public void Create_Misses_WhenSetupOrBikeProcessingInputsChange()
    {
        var factory = CreateFactory(out var rearCalibrationBuilder);
        var bike = CreateBike(shockStroke: 50);
        var setup = CreateSetup(
            bike.Id,
            frontSensorConfigurationJson: LinearForkJson(length: 100),
            rearSensorConfigurationJson: LinearShockJson(length: 50));

        var baseline = factory.Create(setup, bike);
        var changedFrontSensor = factory.Create(
            setup with { FrontSensorConfigurationJson = LinearForkJson(length: 120) },
            bike);
        var changedRearSensor = factory.Create(
            setup with { RearSensorConfigurationJson = LinearShockJson(length: 55) },
            bike);
        var changedHeadAngle = factory.Create(setup, bike with { HeadAngle = bike.HeadAngle + 1 });
        var changedForkStroke = factory.Create(setup, bike with { ForkStroke = bike.ForkStroke + 10 });
        var changedShockStroke = factory.Create(setup, bike with { ShockStroke = bike.ShockStroke + 5 });

        Assert.NotSame(baseline, changedFrontSensor);
        Assert.NotSame(baseline, changedRearSensor);
        Assert.NotSame(baseline, changedHeadAngle);
        Assert.NotSame(baseline, changedForkStroke);
        Assert.NotSame(baseline, changedShockStroke);
        Assert.Equal(6, rearCalibrationBuilder.Calls);
    }

    [Fact]
    public void Create_Misses_WhenRearSuspensionPayloadChanges()
    {
        var factory = CreateFactory(out var rearCalibrationBuilder);
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var linkageBike = CreateBike(
            rearSuspension: new RearSuspensionSpec.Linkage(linkage),
            shockStroke: linkage.ShockStroke);
        var setup = CreateSetup(linkageBike.Id);
        var changedLinkageBike = linkageBike with
        {
            RearSuspension = new RearSuspensionSpec.Linkage(linkage.WithShockStroke(linkage.ShockStroke + 0.1)),
        };
        var leverageBike = linkageBike with
        {
            ShockStroke = 20,
            RearSuspension = new RearSuspensionSpec.LeverageRatio(
                TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50))),
        };
        var changedLeverageBike = leverageBike with
        {
            RearSuspension = new RearSuspensionSpec.LeverageRatio(
                TestSnapshots.LeverageRatioCurve((0, 0), (10, 20), (20, 45))),
        };

        var baselineLinkage = factory.Create(setup, linkageBike);
        var changedLinkage = factory.Create(setup, changedLinkageBike);
        var baselineLeverage = factory.Create(setup, leverageBike);
        var changedLeverage = factory.Create(setup, changedLeverageBike);

        Assert.NotSame(baselineLinkage, changedLinkage);
        Assert.NotSame(baselineLeverage, changedLeverage);
        Assert.Equal(4, rearCalibrationBuilder.Calls);
    }

    [Fact]
    public void Create_SeparatesHardtailDraftAndCompleteRearSuspensions()
    {
        var factory = CreateFactory(out var rearCalibrationBuilder);
        var bikeId = Guid.NewGuid();
        var setup = CreateSetup(bikeId);
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var hardtail = CreateBike(id: bikeId, rearSuspension: new RearSuspensionSpec.Hardtail());
        var linkageDraft = hardtail with { RearSuspension = new RearSuspensionSpec.LinkageDraft() };
        var leverageRatioDraft = hardtail with { RearSuspension = new RearSuspensionSpec.LeverageRatioDraft() };
        var completeLinkage = hardtail with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
        };

        var hardtailContext = factory.Create(setup, hardtail);
        var linkageDraftContext = factory.Create(setup, linkageDraft);
        var leverageRatioDraftContext = factory.Create(setup, leverageRatioDraft);
        var completeContext = factory.Create(setup, completeLinkage);

        Assert.NotSame(hardtailContext, linkageDraftContext);
        Assert.NotSame(hardtailContext, leverageRatioDraftContext);
        Assert.NotSame(hardtailContext, completeContext);
        Assert.NotSame(linkageDraftContext, leverageRatioDraftContext);
        Assert.Equal(4, rearCalibrationBuilder.Calls);
    }

    [Fact]
    public void Create_CachesUnsuccessfulRearCalibrationResult()
    {
        var rearCalibrationBuilder = new CountingRearTravelCalibrationBuilder(
            new RearTravelCalibrationBuildResult(false, null, "invalid"));
        var factory = new TelemetryBikeProcessingContextFactory(rearCalibrationBuilder);
        var bike = CreateBike();
        var setup = CreateSetup(bike.Id);

        var first = factory.Create(setup, bike);
        var second = factory.Create(setup, bike);

        Assert.Same(first, second);
        Assert.False(first.RearTravelCalibration.Succeeded);
        Assert.Null(first.RearTravelCalibration.Calibration);
        Assert.Equal(1, rearCalibrationBuilder.Calls);
    }

    private static TelemetryBikeProcessingContextFactory CreateFactory(
        out CountingRearTravelCalibrationBuilder rearCalibrationBuilder)
    {
        rearCalibrationBuilder = new CountingRearTravelCalibrationBuilder(
            new RearTravelCalibrationBuildResult(true, null, null));
        return new TelemetryBikeProcessingContextFactory(rearCalibrationBuilder);
    }

    private static BikeSnapshot CreateBike(
        Guid? id = null,
        RearSuspensionSpec? rearSuspension = null,
        double? shockStroke = null,
        long updated = 1) =>
        TestSnapshots.Bike(id: id, updated: updated) with
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = shockStroke,
            RearSuspension = rearSuspension ?? new RearSuspensionSpec.Hardtail(),
        };

    private static SetupSnapshot CreateSetup(
        Guid bikeId,
        string? frontSensorConfigurationJson = null,
        string? rearSensorConfigurationJson = null) =>
        TestSnapshots.Setup(bikeId: bikeId) with
        {
            FrontSensorConfigurationJson = frontSensorConfigurationJson,
            RearSensorConfigurationJson = rearSensorConfigurationJson,
        };

    private static string LinearForkJson(double length = 100, int resolution = 12) =>
        SensorConfiguration.ToJson(new LinearForkSensorConfiguration
        {
            Length = length,
            Resolution = resolution,
        });

    private static string LinearShockJson(double length = 50, int resolution = 12) =>
        SensorConfiguration.ToJson(new LinearShockSensorConfiguration
        {
            Length = length,
            Resolution = resolution,
        });

    private sealed class CountingRearTravelCalibrationBuilder(
        RearTravelCalibrationBuildResult result) : IRearTravelCalibrationBuilder
    {
        public int Calls { get; private set; }
        public BikeSnapshot? LastBike { get; private set; }

        public RearTravelCalibrationBuildResult TryBuild(SetupSnapshot setup, BikeSnapshot bike)
        {
            Calls++;
            LastBike = bike;
            return result;
        }
    }
}
