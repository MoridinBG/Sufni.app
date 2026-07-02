using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SQLite;
using Sufni.Kinematics;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Bikes.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Infrastructure;
namespace Sufni.App.Bikes.Models;

/// Mutable domain/persistence model. Convert to/from BikeSnapshot via
/// BikeSnapshot.From(Bike) and Bike.FromSnapshot(BikeSnapshot).
[Table("bike")]
public class Bike : Synchronizable
{
    private static readonly ILogger logger = Log.ForContext<Bike>();

    private double? chainstay;
    private byte[] imageBytes = [];

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("head_angle")]
    [Column("head_angle")]
    public double HeadAngle { get; set; }

    [JsonPropertyName("fork_stroke")]
    [Column("fork_stroke")]
    public double? ForkStroke { get; set; }

    [JsonPropertyName("shock_stroke")]
    [Column("shock_stroke")]
    public double? ShockStroke { get; set; }

    [JsonPropertyName("rear_suspension")]
    [Ignore]
    public RearSuspensionSpec RearSuspension { get; set; } = new RearSuspensionSpec.Hardtail();

    [JsonPropertyName("front_compression_damping_cutoff_mm_per_second")]
    [Column("front_compression_damping_cutoff_mm_per_second")]
    public double FrontCompressionDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("front_rebound_damping_cutoff_mm_per_second")]
    [Column("front_rebound_damping_cutoff_mm_per_second")]
    public double FrontReboundDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("rear_compression_damping_cutoff_mm_per_second")]
    [Column("rear_compression_damping_cutoff_mm_per_second")]
    public double RearCompressionDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("rear_rebound_damping_cutoff_mm_per_second")]
    [Column("rear_rebound_damping_cutoff_mm_per_second")]
    public double RearReboundDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonIgnore]
    [Ignore]
    public DampingSpeedCutoffs DampingSpeedCutoffs
    {
        get => DampingSpeedCutoffs.FromValues(
            FrontCompressionDampingCutoffMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond,
            RearReboundDampingCutoffMmPerSecond);
        set
        {
            var clamped = value.ClampValues();
            FrontCompressionDampingCutoffMmPerSecond = clamped.Front.CompressionMmPerSecond;
            FrontReboundDampingCutoffMmPerSecond = clamped.Front.ReboundMmPerSecond;
            RearCompressionDampingCutoffMmPerSecond = clamped.Rear.CompressionMmPerSecond;
            RearReboundDampingCutoffMmPerSecond = clamped.Rear.ReboundMmPerSecond;
        }
    }

    [JsonIgnore]
    [Column("rear_suspension")]
    public string RearSuspensionJson
    {
        get => RearSuspensionJsonCodec.Serialize(RearSuspension);
        set => RearSuspension = RearSuspensionJsonCodec.Deserialize(value);
    }

    [JsonPropertyName("pixels_to_millimeters")]
    [Column("pixels_to_millimeters")]
    public double PixelsToMillimeters { get; set; }

    [JsonPropertyName("front_wheel_diameter")]
    [Column("front_wheel_diameter")]
    public double? FrontWheelDiameterMm { get; set; }

    [JsonPropertyName("rear_wheel_diameter")]
    [Column("rear_wheel_diameter")]
    public double? RearWheelDiameterMm { get; set; }

    [JsonPropertyName("front_wheel_rim_size")]
    [Column("front_wheel_rim_size")]
    public EtrtoRimSize? FrontWheelRimSize { get; set; }

    [JsonPropertyName("front_wheel_tire_width")]
    [Column("front_wheel_tire_width")]
    public double? FrontWheelTireWidth { get; set; }

    [JsonPropertyName("rear_wheel_rim_size")]
    [Column("rear_wheel_rim_size")]
    public EtrtoRimSize? RearWheelRimSize { get; set; }

    [JsonPropertyName("rear_wheel_tire_width")]
    [Column("rear_wheel_tire_width")]
    public double? RearWheelTireWidth { get; set; }

    [JsonPropertyName("image_rotation_degrees")]
    [Column("image_rotation_degrees")]
    public double ImageRotationDegrees { get; set; }

    [JsonIgnore]
    [Ignore]
    public bool HasWheels => FrontWheelDiameterMm.HasValue && RearWheelDiameterMm.HasValue;

    [JsonPropertyName("image")]
    [Column("image")]
    public byte[] ImageBytes
    {
        get => imageBytes;
        set => imageBytes = value ?? [];
    }

    [JsonIgnore]
    [Ignore]
    public double? Chainstay
    {
        get
        {
            chainstay ??= CalculateChainstay();
            return chainstay;
        }
        init => chainstay = value;
    }

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Bike() { }

    public Bike(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public string ToJson()
    {
        return AppJson.SerializeIndented(BikeExportModel.FromBike(this));
    }

    public static Bike FromSnapshot(BikeSnapshot snapshot) => new(snapshot.Id, snapshot.Name)
    {
        HeadAngle = snapshot.HeadAngle,
        ForkStroke = snapshot.ForkStroke,
        ShockStroke = snapshot.ShockStroke,
        RearSuspension = snapshot.RearSuspension,
        FrontCompressionDampingCutoffMmPerSecond = snapshot.FrontCompressionDampingCutoffMmPerSecond,
        FrontReboundDampingCutoffMmPerSecond = snapshot.FrontReboundDampingCutoffMmPerSecond,
        RearCompressionDampingCutoffMmPerSecond = snapshot.RearCompressionDampingCutoffMmPerSecond,
        RearReboundDampingCutoffMmPerSecond = snapshot.RearReboundDampingCutoffMmPerSecond,
        Chainstay = snapshot.Chainstay,
        PixelsToMillimeters = snapshot.PixelsToMillimeters,
        FrontWheelDiameterMm = snapshot.FrontWheelDiameterMm,
        RearWheelDiameterMm = snapshot.RearWheelDiameterMm,
        FrontWheelRimSize = snapshot.FrontWheelRimSize,
        FrontWheelTireWidth = snapshot.FrontWheelTireWidth,
        RearWheelRimSize = snapshot.RearWheelRimSize,
        RearWheelTireWidth = snapshot.RearWheelTireWidth,
        ImageRotationDegrees = snapshot.ImageRotationDegrees,
        ImageBytes = snapshot.ImageBytes,
        Updated = snapshot.Updated,
    };

    public static Bike? FromJson(string json)
    {
        try
        {
            return AppJson.Deserialize<Bike>(json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or LeverageRatioValidationException)
        {
            logger.Warning(ex, "Bike JSON deserialization failed");
            return null;
        }
    }

    private double? CalculateChainstay()
    {
        if (RearSuspension is not RearSuspensionSpec.Linkage linkage)
        {
            return null;
        }

        var bottomBracket = linkage.Spec.Joints.FirstOrDefault(j => j.Type == JointType.BottomBracket);
        var rearWheel = linkage.Spec.Joints.FirstOrDefault(j => j.Type == JointType.RearWheel);
        if (bottomBracket is null || rearWheel is null) return null;

        var dx = rearWheel.X - bottomBracket.X;
        var dy = rearWheel.Y - bottomBracket.Y;
        return double.Hypot(dx, dy);
    }

    public Bike WithRearSuspension(RearSuspensionSpec rearSuspension)
    {
        ArgumentNullException.ThrowIfNull(rearSuspension);

        var snapshot = BikeSnapshot.From(this) with
        {
            RearSuspension = rearSuspension,
        };
        return FromSnapshot(snapshot);
    }

    public Bike WithShockStroke(double? shockStroke)
    {
        var rearSuspension = RearSuspension is RearSuspensionSpec.Linkage linkage && shockStroke.HasValue
            ? new RearSuspensionSpec.Linkage(linkage.Spec.WithShockStroke(shockStroke.Value))
            : RearSuspension;

        var snapshot = BikeSnapshot.From(this) with
        {
            ShockStroke = shockStroke,
            RearSuspension = rearSuspension,
        };
        return FromSnapshot(snapshot);
    }
}

internal sealed class BikeExportModel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("rear_suspension")]
    public RearSuspensionSpec RearSuspension { get; init; } = new RearSuspensionSpec.Hardtail();

    [JsonPropertyName("head_angle")]
    public double HeadAngle { get; init; }

    [JsonPropertyName("fork_stroke")]
    public double? ForkStroke { get; init; }

    [JsonPropertyName("shock_stroke")]
    public double? ShockStroke { get; init; }

    [JsonPropertyName("front_compression_damping_cutoff_mm_per_second")]
    public double FrontCompressionDampingCutoffMmPerSecond { get; init; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("front_rebound_damping_cutoff_mm_per_second")]
    public double FrontReboundDampingCutoffMmPerSecond { get; init; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("rear_compression_damping_cutoff_mm_per_second")]
    public double RearCompressionDampingCutoffMmPerSecond { get; init; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("rear_rebound_damping_cutoff_mm_per_second")]
    public double RearReboundDampingCutoffMmPerSecond { get; init; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [JsonPropertyName("pixels_to_millimeters")]
    public double PixelsToMillimeters { get; init; }

    [JsonPropertyName("front_wheel_diameter")]
    public double? FrontWheelDiameterMm { get; init; }

    [JsonPropertyName("rear_wheel_diameter")]
    public double? RearWheelDiameterMm { get; init; }

    [JsonPropertyName("front_wheel_rim_size")]
    public EtrtoRimSize? FrontWheelRimSize { get; init; }

    [JsonPropertyName("front_wheel_tire_width")]
    public double? FrontWheelTireWidth { get; init; }

    [JsonPropertyName("rear_wheel_rim_size")]
    public EtrtoRimSize? RearWheelRimSize { get; init; }

    [JsonPropertyName("rear_wheel_tire_width")]
    public double? RearWheelTireWidth { get; init; }

    [JsonPropertyName("image_rotation_degrees")]
    public double ImageRotationDegrees { get; init; }

    [JsonPropertyName("image")]
    public byte[] ImageBytes { get; init; } = [];

    public static BikeExportModel FromBike(Bike bike)
    {
        return new BikeExportModel
        {
            Name = bike.Name,
            RearSuspension = bike.RearSuspension,
            HeadAngle = bike.HeadAngle,
            ForkStroke = bike.ForkStroke,
            ShockStroke = bike.ShockStroke,
            FrontCompressionDampingCutoffMmPerSecond = bike.FrontCompressionDampingCutoffMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = bike.FrontReboundDampingCutoffMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = bike.RearCompressionDampingCutoffMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = bike.RearReboundDampingCutoffMmPerSecond,
            PixelsToMillimeters = bike.PixelsToMillimeters,
            FrontWheelDiameterMm = bike.FrontWheelDiameterMm,
            RearWheelDiameterMm = bike.RearWheelDiameterMm,
            FrontWheelRimSize = bike.FrontWheelRimSize,
            FrontWheelTireWidth = bike.FrontWheelTireWidth,
            RearWheelRimSize = bike.RearWheelRimSize,
            RearWheelTireWidth = bike.RearWheelTireWidth,
            ImageRotationDegrees = bike.ImageRotationDegrees,
            ImageBytes = bike.ImageBytes
        };
    }

    public Bike ToBike()
    {
        var bike = new Bike(Guid.NewGuid(), Name)
        {
            RearSuspension = RearSuspension,
            HeadAngle = HeadAngle,
            ForkStroke = ForkStroke,
            ShockStroke = ShockStroke,
            FrontCompressionDampingCutoffMmPerSecond = FrontCompressionDampingCutoffMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = FrontReboundDampingCutoffMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = RearCompressionDampingCutoffMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = RearReboundDampingCutoffMmPerSecond,
            PixelsToMillimeters = PixelsToMillimeters,
            FrontWheelDiameterMm = FrontWheelDiameterMm,
            RearWheelDiameterMm = RearWheelDiameterMm,
            FrontWheelRimSize = FrontWheelRimSize,
            FrontWheelTireWidth = FrontWheelTireWidth,
            RearWheelRimSize = RearWheelRimSize,
            RearWheelTireWidth = RearWheelTireWidth,
            ImageRotationDegrees = ImageRotationDegrees,
            ImageBytes = ImageBytes
        };
        return bike;
    }
}
