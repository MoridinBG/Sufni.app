using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using SQLite;
using Sufni.Kinematics;

namespace Sufni.App.Models;

[Table("bike")]
public class Bike : Synchronizable
{
    private double? chainstay;

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

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
    public double? ShockStroke
    {
        get
        {
            return Linkage?.ShockStroke;
        }
        set
        {
            if (value is null || Linkage is null) return;
            Linkage.ShockStroke = value.Value;
        }
    }

    [JsonPropertyName("linkage")]
    [Ignore]
    public Linkage? Linkage { get; set; }

    [JsonIgnore]
    [Column("linkage")]
    public string? LinkageJson
    {
        get
        {
            return Linkage?.ToJson();
        }
        set
        {
            if (value is null) return;
            Linkage = Linkage.FromJson(value);
        }
    }

    [JsonPropertyName("pixels_to_millimeters")]
    [Column("pixels_to_millimeters")]
    public double PixelsToMillimeters { get; set; }

    [JsonPropertyName("image")]
    [Column("image")]
    public byte[] ImageBytes
    {
        get
        {
            if (Image is null) return [];
            using var ms = new MemoryStream();
            Image.Save(ms);
            return ms.ToArray();
        }
        set
        {
            var stream = new MemoryStream(value);
            Image = new Bitmap(stream);
        }
    }

    [JsonIgnore]
    [Ignore]
    public Bitmap? Image { get; set; }

    [JsonIgnore]
    [Ignore]
    public double? Chainstay
    {
        get
        {
            chainstay ??= CalculateChainstay();
            return chainstay;
        }
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
        return JsonSerializer.Serialize(this, Linkage.SerializerOptions);
    }

    public static Bike? FromJson(string json)
    {
        var bike = JsonSerializer.Deserialize<Bike>(json, Linkage.SerializerOptions);
        bike?.Linkage?.ResolveJoints();
        return bike;
    }

    private double? CalculateChainstay()
    {
        var bottomBracket = Linkage?.Joints.FirstOrDefault(j => j.Type == JointType.BottomBracket);
        var rearWheel = Linkage?.Joints.FirstOrDefault(j => j.Type == JointType.RearWheel);
        if (bottomBracket is null || rearWheel is null) return null;

        var dx = rearWheel.X - bottomBracket.X;
        var dy = rearWheel.Y - bottomBracket.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
