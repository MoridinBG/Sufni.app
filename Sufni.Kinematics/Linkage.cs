using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public class Linkage
{
    [JsonPropertyName("joints")] public List<Joint> Joints { get; set; } = [];
    [JsonPropertyName("links")] public List<Link> Links { get; set; } = [];
    [JsonPropertyName("shock")] public Link Shock { get; set; } = new("", "");
    [JsonPropertyName("shock_stroke")] public double ShockStroke { get; set; }

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static Linkage LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return FromJson(json);
    }

    public static Linkage FromJson(string json)
    {
        var linkage = JsonSerializer.Deserialize<Linkage>(json, SerializerOptions);
        linkage!.ResolveJoints();

        return linkage;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    public void ResolveJoints()
    {
        // Convert points list to a dictionary for fast lookup
        var pointMap = new Dictionary<string, Joint>();
        foreach (var point in Joints)
        {
            pointMap[point.Name!] = point;
        }

        // Resolve links using the dictionary
        foreach (var link in Links)
        {
            link.ResolveJoints(pointMap);
        }
        Shock.ResolveJoints(pointMap);
    }
}
