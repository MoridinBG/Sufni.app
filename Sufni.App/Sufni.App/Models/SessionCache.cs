using System;
using SQLite;
using Sufni.App.SessionDetails;

namespace Sufni.App.Models;

[Table("session_cache")]
public class SessionCache
{
    [Column("session_id"), PrimaryKey] public Guid SessionId { get; set; }
    [Column("front_travel_histogram")] public string? FrontTravelHistogram { get; set; }
    [Column("rear_travel_histogram")] public string? RearTravelHistogram { get; set; }
    [Column("front_velocity_histogram")] public string? FrontVelocityHistogram { get; set; }
    [Column("rear_velocity_histogram")] public string? RearVelocityHistogram { get; set; }
    [Column("compression_balance")] public string? CompressionBalance { get; set; }
    [Column("rebound_balance")] public string? ReboundBalance { get; set; }
    [Column("front_hsc_percentage")] public double? FrontHscPercentage { get; set; }
    [Column("rear_hsc_percentage")] public double? RearHscPercentage { get; set; }
    [Column("front_lsc_percentage")] public double? FrontLscPercentage { get; set; }
    [Column("rear_lsc_percentage")] public double? RearLscPercentage { get; set; }
    [Column("front_lsr_percentage")] public double? FrontLsrPercentage { get; set; }
    [Column("rear_lsr_percentage")] public double? RearLsrPercentage { get; set; }
    [Column("front_hsr_percentage")] public double? FrontHsrPercentage { get; set; }
    [Column("rear_hsr_percentage")] public double? RearHsrPercentage { get; set; }
    [Column("front_compression_damping_cutoff_mm_per_second")]
    public double FrontCompressionDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;
    [Column("front_rebound_damping_cutoff_mm_per_second")]
    public double FrontReboundDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;
    [Column("rear_compression_damping_cutoff_mm_per_second")]
    public double RearCompressionDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;
    [Column("rear_rebound_damping_cutoff_mm_per_second")]
    public double RearReboundDampingCutoffMmPerSecond { get; set; } = DampingSpeedCutoffs.DefaultMmPerSecond;

    [Ignore]
    public SessionDamperPercentages DamperPercentages
    {
        get => new(
            FrontHscPercentage,
            RearHscPercentage,
            FrontLscPercentage,
            RearLscPercentage,
            FrontLsrPercentage,
            RearLsrPercentage,
            FrontHsrPercentage,
            RearHsrPercentage);
        set
        {
            FrontHscPercentage = value.FrontHscPercentage;
            RearHscPercentage = value.RearHscPercentage;
            FrontLscPercentage = value.FrontLscPercentage;
            RearLscPercentage = value.RearLscPercentage;
            FrontLsrPercentage = value.FrontLsrPercentage;
            RearLsrPercentage = value.RearLsrPercentage;
            FrontHsrPercentage = value.FrontHsrPercentage;
            RearHsrPercentage = value.RearHsrPercentage;
        }
    }

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
}
