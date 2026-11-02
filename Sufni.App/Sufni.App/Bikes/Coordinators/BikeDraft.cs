using System;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;

namespace Sufni.App.Bikes.Coordinators;

/// <summary>The authored fields submitted by a bike editor; damping cutoffs have a separate command.</summary>
public sealed record BikeDraft(
    Guid Id,
    string Name,
    double? HeadAngle,
    double? ForkStroke,
    double? ShockStroke,
    RearSuspensionSpec RearSuspension,
    double PixelsToMillimeters,
    WheelSpec? FrontWheel,
    WheelSpec? RearWheel,
    double ImageRotationDegrees,
    byte[] ImageBytes)
{
    private readonly byte[] imageBytes = [.. ImageBytes];

    // Imported cutoffs seed a new bike; existing-bike saves retain the current store value.
    public DampingSpeedCutoffs InitialDampingSpeedCutoffs { get; init; } = DampingSpeedCutoffs.Default;
    public double? Chainstay => BikeGeometry.Chainstay(RearSuspension);
    public byte[] ImageBytes { get => [.. imageBytes]; init => imageBytes = [.. value]; }
    public ReadOnlyMemory<byte> ImageBytesMemory => imageBytes;

    public static BikeDraft From(BikeSnapshot snapshot) => new(
        snapshot.Id, snapshot.Name, snapshot.HeadAngle, snapshot.ForkStroke, snapshot.ShockStroke,
        snapshot.RearSuspension, snapshot.PixelsToMillimeters, snapshot.FrontWheel, snapshot.RearWheel,
        snapshot.ImageRotationDegrees, snapshot.ImageBytes)
    {
        InitialDampingSpeedCutoffs = snapshot.DampingSpeedCutoffs,
    };

    internal BikeSnapshot ToSnapshot() => new(
        Id, Name, HeadAngle ?? 0, ForkStroke, ShockStroke, RearSuspension, InitialDampingSpeedCutoffs,
        Chainstay, PixelsToMillimeters, FrontWheel, RearWheel, ImageRotationDegrees, imageBytes, 0);

    public bool Equals(BikeDraft? other) => ReferenceEquals(this, other) || other is not null &&
        Id == other.Id && Name == other.Name && HeadAngle == other.HeadAngle && ForkStroke == other.ForkStroke &&
        ShockStroke == other.ShockStroke && RearSuspension == other.RearSuspension &&
        PixelsToMillimeters == other.PixelsToMillimeters && FrontWheel == other.FrontWheel && RearWheel == other.RearWheel &&
        ImageRotationDegrees == other.ImageRotationDegrees && imageBytes.AsSpan().SequenceEqual(other.imageBytes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(Name); hash.Add(HeadAngle); hash.Add(ForkStroke); hash.Add(ShockStroke);
        hash.Add(RearSuspension); hash.Add(PixelsToMillimeters); hash.Add(FrontWheel); hash.Add(RearWheel);
        hash.Add(ImageRotationDegrees); hash.Add(imageBytes.Length);
        return hash.ToHashCode();
    }
}
