using System;
using System.Text.Json.Serialization;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;

namespace Sufni.App.Bikes.Services;

public static class BikeFileFormat
{
    public static string Serialize(Bike bike) => AppJson.SerializeIndented(BikeExportDocument.FromBike(bike));

    public static Bike? Parse(string json)
    {
        var document = AppJson.Deserialize<BikeExportDocument>(json);
        return Parse(document);
    }

    internal static Bike? Parse(BikeExportDocument? document)
    {
        return document?.SchemaVersion == BikeExportDocument.CurrentSchemaVersion
            ? document.ToBike()
            : null;
    }
}

internal sealed record BikeExportDocument(
    [property: JsonPropertyName("schema_version")]
    int SchemaVersion,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("head_angle")]
    double HeadAngle,
    [property: JsonPropertyName("fork_stroke")]
    double? ForkStroke,
    [property: JsonPropertyName("shock_stroke")]
    double? ShockStroke,
    [property: JsonPropertyName("rear_suspension")]
    [property: JsonRequired]
    RearSuspensionSpec RearSuspension,
    [property: JsonPropertyName("damping_speed_cutoffs")]
    DampingSpeedCutoffs DampingSpeedCutoffs,
    [property: JsonPropertyName("pixels_to_millimeters")]
    double PixelsToMillimeters,
    [property: JsonPropertyName("front_wheel")]
    WheelSpec? FrontWheel,
    [property: JsonPropertyName("rear_wheel")]
    WheelSpec? RearWheel,
    [property: JsonPropertyName("image_rotation_degrees")]
    double ImageRotationDegrees,
    [property: JsonPropertyName("image")]
    byte[] ImageBytes)
{
    public const int CurrentSchemaVersion = 2;

    public static BikeExportDocument FromBike(Bike bike)
    {
        return new BikeExportDocument(
            CurrentSchemaVersion,
            bike.Name,
            bike.HeadAngle,
            bike.ForkStroke,
            bike.ShockStroke,
            bike.RearSuspension,
            bike.DampingSpeedCutoffs,
            bike.PixelsToMillimeters,
            WheelSpec.FromValues(bike.FrontWheelDiameterMm, bike.FrontWheelRimSize, bike.FrontWheelTireWidth),
            WheelSpec.FromValues(bike.RearWheelDiameterMm, bike.RearWheelRimSize, bike.RearWheelTireWidth),
            bike.ImageRotationDegrees,
            [.. bike.ImageBytes]);
    }

    public Bike ToBike()
    {
        var bike = new Bike(Guid.NewGuid(), Name)
        {
            RearSuspension = RearSuspension,
            HeadAngle = HeadAngle,
            ForkStroke = ForkStroke,
            ShockStroke = RearSuspension switch
            {
                RearSuspensionSpec.Hardtail => null,
                RearSuspensionSpec.Linkage linkage => linkage.Spec.ShockStroke,
                RearSuspensionSpec.LeverageRatio curve => curve.Spec.MaxShockStroke,
                _ => ShockStroke,
            },
            DampingSpeedCutoffs = DampingSpeedCutoffs,
            PixelsToMillimeters = PixelsToMillimeters,
            FrontWheelDiameterMm = FrontWheel?.DiameterMm,
            RearWheelDiameterMm = RearWheel?.DiameterMm,
            FrontWheelRimSize = FrontWheel?.RimSize,
            FrontWheelTireWidth = FrontWheel?.TireWidth,
            RearWheelRimSize = RearWheel?.RimSize,
            RearWheelTireWidth = RearWheel?.TireWidth,
            ImageRotationDegrees = ImageRotationDegrees,
            ImageBytes = [.. ImageBytes]
        };
        return bike;
    }
}

