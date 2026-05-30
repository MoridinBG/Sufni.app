using System;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.Kinematics;

namespace Sufni.App.Stores;

/// <summary>
/// Immutable view of a bike as currently known to the store. Consumers
/// read from snapshots; to get an updated version, ask the store.
/// </summary>
public sealed record BikeSnapshot(
    Guid Id,
    string Name,
    double HeadAngle,
    double? ForkStroke,
    double? ShockStroke,
    RearSuspensionKind RearSuspensionKind,
    double FrontCompressionDampingCutoffMmPerSecond,
    double FrontReboundDampingCutoffMmPerSecond,
    double RearCompressionDampingCutoffMmPerSecond,
    double RearReboundDampingCutoffMmPerSecond,
    double? Chainstay,
    double PixelsToMillimeters,
    double? FrontWheelDiameterMm,
    double? RearWheelDiameterMm,
    EtrtoRimSize? FrontWheelRimSize,
    double? FrontWheelTireWidth,
    EtrtoRimSize? RearWheelRimSize,
    double? RearWheelTireWidth,
    double ImageRotationDegrees,
    LeverageRatio? LeverageRatio,
    Linkage? Linkage,
    byte[] ImageBytes,
    long Updated)
{
    public DampingSpeedCutoffs DampingSpeedCutoffs => DampingSpeedCutoffs.FromValues(
        FrontCompressionDampingCutoffMmPerSecond,
        FrontReboundDampingCutoffMmPerSecond,
        RearCompressionDampingCutoffMmPerSecond,
        RearReboundDampingCutoffMmPerSecond);

    public static BikeSnapshot From(Bike bike) => new(
        bike.Id,
        bike.Name,
        bike.HeadAngle,
        bike.ForkStroke,
        bike.ShockStroke,
        bike.RearSuspensionKind,
        bike.FrontCompressionDampingCutoffMmPerSecond,
        bike.FrontReboundDampingCutoffMmPerSecond,
        bike.RearCompressionDampingCutoffMmPerSecond,
        bike.RearReboundDampingCutoffMmPerSecond,
        bike.Chainstay,
        bike.PixelsToMillimeters,
        bike.FrontWheelDiameterMm,
        bike.RearWheelDiameterMm,
        bike.FrontWheelRimSize,
        bike.FrontWheelTireWidth,
        bike.RearWheelRimSize,
        bike.RearWheelTireWidth,
        bike.ImageRotationDegrees,
        bike.LeverageRatio,
        bike.Linkage,
        bike.ImageBytes,
        bike.Updated);
}
