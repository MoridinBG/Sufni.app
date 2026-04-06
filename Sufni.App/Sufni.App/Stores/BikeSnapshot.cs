using System;
using Avalonia.Media.Imaging;
using Sufni.App.Models;
using Sufni.Kinematics;

namespace Sufni.App.Stores;

/// <summary>
/// Immutable view of a bike as currently known to the store. Consumers
/// (rows, editors, queries) read from snapshots; they never mutate them.
/// To get an updated version, ask the store.
///
/// The <see cref="Updated"/> field is the version used for optimistic
/// conflict detection at save time — see the "Store / editor snapshot
/// and conflict protocol" section of REFACTOR-PLAN.md.
/// </summary>
public sealed record BikeSnapshot(
    Guid Id,
    string Name,
    double HeadAngle,
    double? ForkStroke,
    double? ShockStroke,
    double? Chainstay,
    double PixelsToMillimeters,
    Linkage? Linkage,
    Bitmap? Image,
    long Updated)
{
    public static BikeSnapshot From(Bike bike) => new(
        bike.Id,
        bike.Name,
        bike.HeadAngle,
        bike.ForkStroke,
        bike.ShockStroke,
        bike.Chainstay,
        bike.PixelsToMillimeters,
        bike.Linkage,
        bike.Image,
        bike.Updated);
}
