using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models.Telemetry;

public record CalibrationMethodProperties(
    [property: JsonPropertyName("inputs")] List<string> Inputs,
    [property: JsonPropertyName("intermediates")] Dictionary<string, string> Intermediates,
    [property: JsonPropertyName("expression")] string Expression);

[Table("calibration_method")]
public class CalibrationMethod : Synchronizable
{
    public static readonly Guid FractionId = Guid.Parse("230e04a092ce42189a3c23bf3cde2b05");
    public static readonly Guid PercentageId = Guid.Parse("c619045af435427797cb1e2c1fddcfeb");
    public static readonly Guid LinearId = Guid.Parse("3e799d5a5652430e900c06a3277ab1dc");
    public static readonly Guid LinearPotmeterId = Guid.Parse("347cf9a82df54ef8b63936b36357f08f");
    public static readonly Guid IsoscelesId = Guid.Parse("12f4a1b922f74524abcbdaa99a5c1c3a");
    public static readonly Guid TriangleId = Guid.Parse("9a27abc4125148a2b64989fb315ca2de");

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public CalibrationMethod() { }

    public CalibrationMethod(Guid id, string name, string description, CalibrationMethodProperties properties)
    {
        Id = id;
        Name = name;
        Description = description;
        Properties = properties;
    }

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [Column("description")]

    public string Description { get; set; } = null!;

    [JsonPropertyName("properties")]
    [Ignore]
    public CalibrationMethodProperties Properties { get; set; } = null!;

    [JsonIgnore]
    [Column("data")]
    public string? PropertiesRaw
    {
        get => JsonSerializer.Serialize(Properties);
        set
        {
            Debug.Assert(value != null, nameof(value) + " != null");
            Properties = JsonSerializer.Deserialize<CalibrationMethodProperties>(value) ??
                         throw new InvalidOperationException();
        }
    }
}