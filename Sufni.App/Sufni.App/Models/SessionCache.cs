using System;
using SQLite;

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
}