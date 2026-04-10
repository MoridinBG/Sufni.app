using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using SQLite;
using Sufni.App.Models.SensorConfigurations;
using Sufni.Kinematics;

namespace Sufni.App.Models;

[Table("bike")]
public class Bike : Synchronizable
{
    private double? chainstay;
    private Linkage? linkage;

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
    public Linkage? Linkage
    {
        get => linkage;
        set
        {
            linkage = value;
            linkage?.ResolveJoints();
        }
    }

    [JsonIgnore]
    [Column("linkage")]
    public string? LinkageJson
    {
        get => Linkage?.ToJson();
        set
        {
            if (value is null) return;
            Linkage = Linkage.FromJson(value, false); // Linkage's setter will resolve joints.
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
            if (value.Length == 0)
            {
                Image = null;
                return;
            }
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

    public static Bike? FromJson(string json)
    {
        var bike = AppJson.Deserialize<Bike>(json);
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

internal sealed class BikeExportModel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("head_angle")]
    public double HeadAngle { get; init; }

    [JsonPropertyName("fork_stroke")]
    public double? ForkStroke { get; init; }

    [JsonPropertyName("shock_stroke")]
    public double? ShockStroke { get; init; }

    [JsonPropertyName("linkage")]
    public Linkage? Linkage { get; init; }

    [JsonPropertyName("pixels_to_millimeters")]
    public double PixelsToMillimeters { get; init; }

    [JsonPropertyName("image")]
    public byte[] ImageBytes { get; init; } = [];

    public static BikeExportModel FromBike(Bike bike)
    {
        return new BikeExportModel
        {
            Name = bike.Name,
            HeadAngle = bike.HeadAngle,
            ForkStroke = bike.ForkStroke,
            ShockStroke = bike.ShockStroke,
            Linkage = bike.Linkage,
            PixelsToMillimeters = bike.PixelsToMillimeters,
            ImageBytes = bike.ImageBytes
        };
    }
}

internal static class AppJson
{
    public static AppJsonContext Context { get; } = new(CreateOptions());

    public static string Serialize<T>(T? value)
    {
        return JsonSerializer.Serialize(value, typeof(T), Context);
    }

    public static T? Deserialize<T>(string json) where T : class
    {
        return (T?)JsonSerializer.Deserialize(json, typeof(T), Context);
    }

    public static string SerializeIndented<T>(T? value)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        JsonSerializer.Serialize(writer, value, typeof(T), Context);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Bike))]
[JsonSerializable(typeof(BikeExportModel))]
[JsonSerializable(typeof(Board))]
[JsonSerializable(typeof(Setup))]
[JsonSerializable(typeof(SynchronizationData))]
[JsonSerializable(typeof(TrackPoint))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(List<TrackPoint>))]
[JsonSerializable(typeof(PairingRequest))]
[JsonSerializable(typeof(PairingConfirm))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(UnpairRequest))]
[JsonSerializable(typeof(SensorConfiguration))]
[JsonSerializable(typeof(LinearForkSensorConfiguration))]
[JsonSerializable(typeof(RotationalForkSensorConfiguration))]
[JsonSerializable(typeof(LinearShockSensorConfiguration))]
[JsonSerializable(typeof(RotationalShockSensorConfiguration))]
[JsonSerializable(typeof(Linkage))]
[JsonSerializable(typeof(Link))]
[JsonSerializable(typeof(Joint))]
internal partial class AppJsonContext : JsonSerializerContext;
