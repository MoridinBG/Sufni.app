using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Setups.Coordinators;

public class SetupSaveRulesTests
{
    [Theory]
    [InlineData(SetupValidationFailureReason.Name)]
    [InlineData(SetupValidationFailureReason.Board)]
    [InlineData(SetupValidationFailureReason.Bike)]
    [InlineData(SetupValidationFailureReason.Sensor)]
    [InlineData(SetupValidationFailureReason.Calibration)]
    [InlineData(SetupValidationFailureReason.RearCompatibility)]
    public void Validate_ExplainsRequiredInputAndCompatibilityRejections(SetupValidationFailureReason reason)
    {
        var bike = TestSnapshots.Bike();
        var valid = new SetupDraft(Guid.NewGuid(), "setup", bike.Id, Guid.NewGuid(), new SensorDraft.LinearFork(150, 12), null);
        var draft = reason switch
        {
            SetupValidationFailureReason.Name => valid with { Name = null },
            SetupValidationFailureReason.Board => valid with { BoardId = null },
            SetupValidationFailureReason.Bike => valid with { BikeId = Guid.NewGuid() },
            SetupValidationFailureReason.Sensor => valid with { FrontSensor = null },
            SetupValidationFailureReason.Calibration => valid with { FrontSensor = new SensorDraft.LinearFork(null, 12) },
            _ => valid with { RearSensor = new SensorDraft.LinearShock(50, 12) },
        };

        Assert.Null(SetupSaveRules.Validate(valid, bike));
        Assert.Equal(reason, SetupSaveRules.Validate(draft, bike));
    }

    [Fact]
    public void Validate_RequiresShockStrokeSensorForLeverageRatioBike()
    {
        var bike = TestSnapshots.LeverageRatioBike(TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)), shockStroke: 20);
        var draft = new SetupDraft(Guid.NewGuid(), "setup", bike.Id, Guid.NewGuid(), null, new SensorDraft.LinearShock(50, 12));

        Assert.Equal(SetupValidationFailureReason.RearCompatibility, SetupSaveRules.Validate(draft, bike));
        Assert.Null(SetupSaveRules.Validate(draft with { RearSensor = new SensorDraft.LinearShock(50, 12, true) }, bike));
    }

    [Fact]
    public void Validate_RequiresDistinctExistingJointsForRotationalShock()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bike = TestSnapshots.Bike() with { RearSuspension = new RearSuspensionSpec.Linkage(linkage) };
        var sensor = new SensorDraft.RotationalShock(linkage.Joints[0].Name, linkage.Joints[1].Name, linkage.Joints[2].Name);
        var draft = new SetupDraft(Guid.NewGuid(), "setup", bike.Id, Guid.NewGuid(), null, sensor);

        Assert.Null(SetupSaveRules.Validate(draft, bike));
        Assert.Equal(SetupValidationFailureReason.Calibration,
            SetupSaveRules.Validate(draft with { RearSensor = sensor with { AdjacentJoint1 = sensor.CentralJoint } }, bike));
        Assert.Equal(SetupValidationFailureReason.RearCompatibility,
            SetupSaveRules.Validate(draft with { RearSensor = sensor with { AdjacentJoint1 = "missing" } }, bike));
    }
}
