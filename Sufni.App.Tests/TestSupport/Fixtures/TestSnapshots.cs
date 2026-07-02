using Sufni.Kinematics;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Bikes.Models;
namespace Sufni.App.Tests.TestSupport.Fixtures;

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
        RearSuspension: new RearSuspensionSpec.Hardtail(),
        FrontCompressionDampingCutoffMmPerSecond: 200,
        FrontReboundDampingCutoffMmPerSecond: 200,
        RearCompressionDampingCutoffMmPerSecond: 200,
        RearReboundDampingCutoffMmPerSecond: 200,
        Chainstay: null,
        PixelsToMillimeters: 0,
        FrontWheel: null,
        RearWheel: null,
        ImageRotationDegrees: 0,
        ImageBytes: [],
        Updated: updated);

    public static LeverageRatioSpec LeverageRatioCurve(params (double ShockTravelMm, double WheelTravelMm)[] points) =>
        LeverageRatioSpec.FromPoints(points.Select(point => new LeverageRatioPoint(point.ShockTravelMm, point.WheelTravelMm)).ToArray());

    public static BikeSnapshot LeverageRatioBike(
        LeverageRatioSpec leverageRatio,
        double? shockStroke = 60,
        Guid? id = null,
        string name = "test leverage-ratio bike",
        long updated = 1)
    {
        var bike = new Bike(id ?? Guid.NewGuid(), name)
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = shockStroke,
            RearSuspension = new RearSuspensionSpec.LeverageRatio(leverageRatio),
            Updated = updated,
        };

        return BikeSnapshot.From(bike);
    }

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

    public static LinkageSpec FullSuspensionLinkageSpec(bool includeHeadTubeJoints = false)
    {
        var mapping = new JointNameMapping();

        List<JointSpec> joints =
        [
            new(mapping.BottomBracket, JointType.BottomBracket, 0, 0),
            new(mapping.RearWheel, JointType.RearWheel, 4, 0),
            new(mapping.FrontWheel, JointType.FrontWheel, 12, 1),
            new(mapping.ShockEye1, JointType.Floating, 4, 3),
            new(mapping.ShockEye2, JointType.Fixed, 0, 3)
        ];
        if (includeHeadTubeJoints)
        {
            joints.Add(new JointSpec(mapping.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new JointSpec(mapping.HeadTube2, JointType.HeadTube, 9, 5));
        }

        return new LinkageSpec(
            joints,
            [
                new LinkSpec(mapping.BottomBracket, mapping.RearWheel),
                new LinkSpec(mapping.RearWheel, mapping.ShockEye1),
            ],
            new LinkSpec(mapping.ShockEye1, mapping.ShockEye2),
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
        string? processingFingerprintJson = null,
        long updated = 1) => new(
        Id: id ?? Guid.NewGuid(),
        Name: name,
        Description: description,
        SetupId: setupId,
        Timestamp: timestamp,
        FullTrackId: null,
        HasProcessedData: hasProcessedData,
        ProcessingFingerprintJson: processingFingerprintJson,
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
