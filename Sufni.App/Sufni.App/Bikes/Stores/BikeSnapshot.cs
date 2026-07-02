using System;
using Sufni.Kinematics;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Bikes.Models;
namespace Sufni.App.Bikes.Stores;

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
    RearSuspensionSpec RearSuspension,
    double FrontCompressionDampingCutoffMmPerSecond,
    double FrontReboundDampingCutoffMmPerSecond,
    double RearCompressionDampingCutoffMmPerSecond,
    double RearReboundDampingCutoffMmPerSecond,
    double? Chainstay,
    double PixelsToMillimeters,
    WheelSpec? FrontWheel,
    WheelSpec? RearWheel,
    double ImageRotationDegrees,
    byte[] ImageBytes,
    long Updated)
{
    public RearSuspensionKind Kind => RearSuspension.Kind;

    public LinkageSpec? Linkage => (RearSuspension as RearSuspensionSpec.Linkage)?.Spec;

    public LeverageRatioSpec? LeverageRatio => (RearSuspension as RearSuspensionSpec.LeverageRatio)?.Spec;

    public double? FrontWheelDiameterMm
    {
        get => FrontWheel?.DiameterMm;
        init => FrontWheel = WheelSpec.FromValues(value, FrontWheel?.RimSize, FrontWheel?.TireWidth);
    }

    public double? RearWheelDiameterMm
    {
        get => RearWheel?.DiameterMm;
        init => RearWheel = WheelSpec.FromValues(value, RearWheel?.RimSize, RearWheel?.TireWidth);
    }

    public EtrtoRimSize? FrontWheelRimSize
    {
        get => FrontWheel?.RimSize;
        init => FrontWheel = WheelSpec.FromValues(FrontWheel?.DiameterMm, value, FrontWheel?.TireWidth);
    }

    public double? FrontWheelTireWidth
    {
        get => FrontWheel?.TireWidth;
        init => FrontWheel = WheelSpec.FromValues(FrontWheel?.DiameterMm, FrontWheel?.RimSize, value);
    }

    public EtrtoRimSize? RearWheelRimSize
    {
        get => RearWheel?.RimSize;
        init => RearWheel = WheelSpec.FromValues(RearWheel?.DiameterMm, value, RearWheel?.TireWidth);
    }

    public double? RearWheelTireWidth
    {
        get => RearWheel?.TireWidth;
        init => RearWheel = WheelSpec.FromValues(RearWheel?.DiameterMm, RearWheel?.RimSize, value);
    }

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
        bike.RearSuspension,
        bike.FrontCompressionDampingCutoffMmPerSecond,
        bike.FrontReboundDampingCutoffMmPerSecond,
        bike.RearCompressionDampingCutoffMmPerSecond,
        bike.RearReboundDampingCutoffMmPerSecond,
        bike.Chainstay,
        bike.PixelsToMillimeters,
        WheelSpec.FromValues(bike.FrontWheelDiameterMm, bike.FrontWheelRimSize, bike.FrontWheelTireWidth),
        WheelSpec.FromValues(bike.RearWheelDiameterMm, bike.RearWheelRimSize, bike.RearWheelTireWidth),
        bike.ImageRotationDegrees,
        [.. bike.ImageBytes],
        bike.Updated);
}

public sealed record WheelSpec(
    double? DiameterMm,
    EtrtoRimSize? RimSize,
    double? TireWidth)
{
    public static WheelSpec? FromValues(
        double? diameterMm,
        EtrtoRimSize? rimSize,
        double? tireWidth) =>
        diameterMm.HasValue || rimSize.HasValue || tireWidth.HasValue
            ? new WheelSpec(diameterMm, rimSize, tireWidth)
            : null;
}
