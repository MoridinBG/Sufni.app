using Sufni.App.Stores;

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
