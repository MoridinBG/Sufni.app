using System;
using Avalonia.Media.Imaging;
using Sufni.App.Models;
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
    Bitmap? Image,
    long Updated) : IVersionedSnapshot
{
    public static BikeSnapshot From(Bike bike) => new(
        bike.Id,
        bike.Name,
        bike.HeadAngle,
        bike.ForkStroke,
        bike.ShockStroke,
        bike.RearSuspensionKind,
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
        bike.Image,
        bike.Updated);

    public bool TryResolveRearSuspension(out RearSuspension? rearSuspension, out string? errorMessage)
    {
        switch (RearSuspensionKind)
        {
            case RearSuspensionKind.None when Linkage is null && LeverageRatio is null:
                rearSuspension = null;
                errorMessage = null;
                return true;

            case RearSuspensionKind.Linkage when Linkage is not null && LeverageRatio is null:
                rearSuspension = new LinkageRearSuspension(Linkage);
                errorMessage = null;
                return true;

            case RearSuspensionKind.LeverageRatio when Linkage is null && LeverageRatio is not null:
                rearSuspension = new LeverageRatioRearSuspension(LeverageRatio);
                errorMessage = null;
                return true;

            default:
                rearSuspension = null;
                errorMessage = "Rear suspension kind does not match the stored rear suspension payload.";
                return false;
        }
    }
}
