using Sufni.App.Stores;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Infrastructure;

/// <summary>
/// Cheap factory helpers for the snapshot records used by coordinators
/// and editors. Defaults are intentionally minimal — tests override
/// only what they care about via <c>with</c> expressions.
/// </summary>
public static class TestSnapshots
{
    public static BikeSnapshot Bike(
        Guid? id = null,
        string name = "test bike",
        long updated = 1) => new(
        Id: id ?? Guid.NewGuid(),
        Name: name,
        HeadAngle: 65,
        ForkStroke: 160,
        ShockStroke: null,
        Chainstay: null,
        PixelsToMillimeters: 0,
        FrontWheelDiameterMm: null,
        RearWheelDiameterMm: null,
        FrontWheelRimSize: null,
        FrontWheelTireWidth: null,
        RearWheelRimSize: null,
        RearWheelTireWidth: null,
        ImageRotationDegrees: 0,
        Linkage: null,
        Image: null,
        Updated: updated);

    public static SetupSnapshot Setup(
        Guid? id = null,
        string name = "test setup",
        Guid? bikeId = null,
        Guid? boardId = null,
        long updated = 1) => new(
        Id: id ?? Guid.NewGuid(),
        Name: name,
        BikeId: bikeId ?? Guid.NewGuid(),
        BoardId: boardId,
        FrontSensorConfigurationJson: null,
        RearSensorConfigurationJson: null,
        Updated: updated);

    public static Linkage FullSuspensionLinkage(bool includeHeadTubeJoints = false)
    {
        var mapping = new JointNameMapping();
        var bottomBracket = new Joint(mapping.BottomBracket, JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint(mapping.RearWheel, JointType.RearWheel, 4, 0);
        var frontWheel = new Joint(mapping.FrontWheel, JointType.FrontWheel, 12, 1);
        var shockEye1 = new Joint(mapping.ShockEye1, JointType.Floating, 4, 3);
        var shockEye2 = new Joint(mapping.ShockEye2, JointType.Fixed, 0, 3);

        List<Joint> joints = [bottomBracket, rearWheel, frontWheel, shockEye1, shockEye2];
        if (includeHeadTubeJoints)
        {
            joints.Add(new Joint(mapping.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new Joint(mapping.HeadTube2, JointType.HeadTube, 9, 5));
        }

        return Linkage.CreateResolved(
            joints,
            [
                new Link(bottomBracket, rearWheel),
                new Link(rearWheel, shockEye1),
            ],
            new Link(shockEye1, shockEye2),
            0.5);
    }

    public static double WheelDiameter(EtrtoRimSize rimSize, double tireWidth) =>
        Math.Round(rimSize.CalculateTotalDiameterMm(tireWidth), 1);

    public static SessionSnapshot Session(
        Guid? id = null,
        string name = "test session",
        string description = "",
        Guid? setupId = null,
        long? timestamp = null,
        bool hasProcessedData = false,
        long updated = 1) => new(
        Id: id ?? Guid.NewGuid(),
        Name: name,
        Description: description,
        SetupId: setupId,
        Timestamp: timestamp,
        FullTrackId: null,
        HasProcessedData: hasProcessedData,
        FrontSpringRate: null,
        FrontHighSpeedCompression: null,
        FrontLowSpeedCompression: null,
        FrontLowSpeedRebound: null,
        FrontHighSpeedRebound: null,
        RearSpringRate: null,
        RearHighSpeedCompression: null,
        RearLowSpeedCompression: null,
        RearLowSpeedRebound: null,
        RearHighSpeedRebound: null,
        Updated: updated);
}
