using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SQLite;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Bikes.Stores;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Bikes.Models;

/// Mutable domain/persistence model. Convert to/from BikeSnapshot via
/// BikeSnapshot.From(Bike) and Bike.FromSnapshot(BikeSnapshot).
[Table("bike")]
public class Bike : Synchronizable
{

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
    [JsonRequired]
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

    [JsonPropertyName("image")]
    [Column("image")]
    public byte[] ImageBytes
    {
        get => imageBytes;
        set => imageBytes = value ?? [];
    }

    [JsonIgnore]
    [Ignore]
    public double? Chainstay => BikeGeometry.Chainstay(RearSuspension);

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Bike() { }

    public Bike(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public static Bike FromSnapshot(BikeSnapshot snapshot) => new(snapshot.Id, snapshot.Name)
    {
        HeadAngle = snapshot.HeadAngle,
        ForkStroke = snapshot.ForkStroke,
        ShockStroke = snapshot.ShockStroke,
        RearSuspension = snapshot.RearSuspension,
        DampingSpeedCutoffs = snapshot.DampingSpeedCutoffs,
        PixelsToMillimeters = snapshot.PixelsToMillimeters,
        FrontWheelDiameterMm = snapshot.FrontWheelDiameterMm,
        RearWheelDiameterMm = snapshot.RearWheelDiameterMm,
        FrontWheelRimSize = snapshot.FrontWheelRimSize,
        FrontWheelTireWidth = snapshot.FrontWheelTireWidth,
        RearWheelRimSize = snapshot.RearWheelRimSize,
        RearWheelTireWidth = snapshot.RearWheelTireWidth,
        ImageRotationDegrees = snapshot.ImageRotationDegrees,
        ImageBytes = snapshot.CopyImageBytes(),
        Updated = snapshot.Updated,
    };

}
