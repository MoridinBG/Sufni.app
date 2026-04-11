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
    double? Chainstay,
    double PixelsToMillimeters,
    double? FrontWheelDiameterMm,
    double? RearWheelDiameterMm,
    EtrtoRimSize? FrontWheelRimSize,
    double? FrontWheelTireWidth,
    EtrtoRimSize? RearWheelRimSize,
    double? RearWheelTireWidth,
    double ImageRotationDegrees,
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
        bike.Chainstay,
        bike.PixelsToMillimeters,
        bike.FrontWheelDiameterMm,
        bike.RearWheelDiameterMm,
        bike.FrontWheelRimSize,
        bike.FrontWheelTireWidth,
        bike.RearWheelRimSize,
        bike.RearWheelTireWidth,
        bike.ImageRotationDegrees,
        bike.Linkage,
        bike.Image,
        bike.Updated);
}
