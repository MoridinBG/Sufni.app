using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models;

[Table("setup")]
public class Setup : Synchronizable
{
    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Setup()
    {
    }

    public Setup(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonIgnore]
    [Column("front_coefficients")]
    public string? FrontCoefficientsJson { get; set; }

    [JsonPropertyName("front_coefficients")]
    [Ignore]
    public double[]? FrontCoefficients
    {
        get => FrontCoefficientsJson is null ? null : JsonSerializer.Deserialize<double[]>(FrontCoefficientsJson);
        set => FrontCoefficientsJson = value is null ? null : JsonSerializer.Serialize(value);
    }

    [JsonIgnore]
    [Column("rear_coefficients")]
    public string? RearCoefficientsJson { get; set; }

    [JsonPropertyName("rear_coefficients")]
    [Ignore]
    public double[]? RearCoefficients
    {
        get => RearCoefficientsJson is null ? null : JsonSerializer.Deserialize<double[]>(RearCoefficientsJson);
        set => RearCoefficientsJson = value is null ? null : JsonSerializer.Serialize(value);
    }

    [JsonPropertyName("front_max_travel")]
    [Column("front_max_travel")]
    public double FrontMaxTravel { get; set; }

    [JsonPropertyName("rear_max_travel")]
    [Column("rear_max_travel")]
    public double RearMaxTravel { get; set; }

    [JsonPropertyName("head_angle")]
    [Column("head_angle")]
    public double HeadAngle { get; set; }
}