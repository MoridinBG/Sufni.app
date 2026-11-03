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
        DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
        Chainstay: null,
        PixelsToMillimeters: 0,
        FrontWheel: null,
        RearWheel: null,
        ImageRotationDegrees: 0,
        HasImage: false,
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
        long updated = 1) => new(
        Id: id ?? Guid.NewGuid(),
        Name: name,
        BikeId: bikeId ?? Guid.NewGuid(),
        FrontSensorConfigurationJson: null,
        RearSensorConfigurationJson: null,
        Updated: updated);

    public static LinkageSpec FullSuspensionLinkageSpec(bool includeHeadTubeJoints = false) =>
        Sufni.Kinematics.Tests.TestLinkages.FullSuspensionLinkageSpec(includeHeadTubeJoints);

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
