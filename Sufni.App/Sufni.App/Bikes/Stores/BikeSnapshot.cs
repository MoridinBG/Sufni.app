using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    DampingSpeedCutoffs DampingSpeedCutoffs,
    double? Chainstay,
    double PixelsToMillimeters,
    WheelSpec? FrontWheel,
    WheelSpec? RearWheel,
    double ImageRotationDegrees,
    byte[] ImageBytes,
    long Updated)
{
    private readonly byte[] imageBytes = [.. ImageBytes];

    public RearSuspensionKind Kind => RearSuspension.Kind;

    public LinkageSpec? Linkage => (RearSuspension as RearSuspensionSpec.Linkage)?.Spec;

    public LeverageRatioSpec? LeverageRatio => (RearSuspension as RearSuspensionSpec.LeverageRatio)?.Spec;

    public byte[] ImageBytes
    {
        get => CopyImageBytes();
        init => imageBytes = value is null ? [] : [.. value];
    }

    public int ImageByteCount => imageBytes.Length;

    public ReadOnlyMemory<byte> ImageBytesMemory => imageBytes;

    public ReadOnlySpan<byte> ImageBytesSpan => imageBytes;

    public byte[] CopyImageBytes() => [.. imageBytes];

    public bool Equals(BikeSnapshot? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Id == other.Id &&
            Name == other.Name &&
            HeadAngle.Equals(other.HeadAngle) &&
            Nullable.Equals(ForkStroke, other.ForkStroke) &&
            Nullable.Equals(ShockStroke, other.ShockStroke) &&
            EqualityComparer<RearSuspensionSpec>.Default.Equals(RearSuspension, other.RearSuspension) &&
            EqualityComparer<DampingSpeedCutoffs>.Default.Equals(DampingSpeedCutoffs, other.DampingSpeedCutoffs) &&
            Nullable.Equals(Chainstay, other.Chainstay) &&
            PixelsToMillimeters.Equals(other.PixelsToMillimeters) &&
            EqualityComparer<WheelSpec?>.Default.Equals(FrontWheel, other.FrontWheel) &&
            EqualityComparer<WheelSpec?>.Default.Equals(RearWheel, other.RearWheel) &&
            ImageRotationDegrees.Equals(other.ImageRotationDegrees) &&
            imageBytes.AsSpan().SequenceEqual(other.imageBytes) &&
            Updated == other.Updated;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(HeadAngle);
        hash.Add(ForkStroke);
        hash.Add(ShockStroke);
        hash.Add(RearSuspension);
        hash.Add(DampingSpeedCutoffs);
        hash.Add(Chainstay);
        hash.Add(PixelsToMillimeters);
        hash.Add(FrontWheel);
        hash.Add(RearWheel);
        hash.Add(ImageRotationDegrees);
        hash.Add(imageBytes.Length);
        hash.Add(Updated);
        return hash.ToHashCode();
    }

    public double FrontCompressionDampingCutoffMmPerSecond
    {
        get => DampingSpeedCutoffs.Front.CompressionMmPerSecond;
        init => DampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(
            value,
            DampingSpeedCutoffs.Front.ReboundMmPerSecond,
            DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
            DampingSpeedCutoffs.Rear.ReboundMmPerSecond);
    }

    public double FrontReboundDampingCutoffMmPerSecond
    {
        get => DampingSpeedCutoffs.Front.ReboundMmPerSecond;
        init => DampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(
            DampingSpeedCutoffs.Front.CompressionMmPerSecond,
            value,
            DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
            DampingSpeedCutoffs.Rear.ReboundMmPerSecond);
    }

    public double RearCompressionDampingCutoffMmPerSecond
    {
        get => DampingSpeedCutoffs.Rear.CompressionMmPerSecond;
        init => DampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(
            DampingSpeedCutoffs.Front.CompressionMmPerSecond,
            DampingSpeedCutoffs.Front.ReboundMmPerSecond,
            value,
            DampingSpeedCutoffs.Rear.ReboundMmPerSecond);
    }

    public double RearReboundDampingCutoffMmPerSecond
    {
        get => DampingSpeedCutoffs.Rear.ReboundMmPerSecond;
        init => DampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(
            DampingSpeedCutoffs.Front.CompressionMmPerSecond,
            DampingSpeedCutoffs.Front.ReboundMmPerSecond,
            DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
            value);
    }

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

    public static BikeSnapshot From(Bike bike) => new(
        bike.Id,
        bike.Name,
        bike.HeadAngle,
        bike.ForkStroke,
        bike.ShockStroke,
        bike.RearSuspension,
        bike.DampingSpeedCutoffs,
        bike.Chainstay,
        bike.PixelsToMillimeters,
        WheelSpec.FromValues(bike.FrontWheelDiameterMm, bike.FrontWheelRimSize, bike.FrontWheelTireWidth),
        WheelSpec.FromValues(bike.RearWheelDiameterMm, bike.RearWheelRimSize, bike.RearWheelTireWidth),
        bike.ImageRotationDegrees,
        bike.ImageBytes,
        bike.Updated);
}

public sealed record WheelSpec(
    [property: JsonPropertyName("diameter_mm")]
    double? DiameterMm,
    [property: JsonPropertyName("rim_size")]
    EtrtoRimSize? RimSize,
    [property: JsonPropertyName("tire_width")]
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
