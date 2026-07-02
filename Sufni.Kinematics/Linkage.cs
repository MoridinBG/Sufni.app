using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public class Linkage
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    #region Public properties

    [JsonPropertyName("joints")] public List<Joint> Joints { get; set; } = [];
    [JsonPropertyName("links")] public List<Link> Links { get; set; } = [];
    [JsonPropertyName("shock")] public Link Shock { get; set; } = new("", "");
    [JsonPropertyName("shock_stroke")] public double ShockStroke { get; set; }

    #endregion Public properties

    #region Initializers

    public static Linkage LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return FromJson(json);
    }

    public static Linkage FromJson(string json, bool resolve = true)
    {
        var linkage = JsonSerializer.Deserialize<Linkage>(json, SerializerOptions);
        if (resolve) linkage!.ResolveJoints();

        return linkage!;
    }

    public static Linkage CreateResolved(
        IEnumerable<Joint> joints,
        IEnumerable<Link> links,
        Link shock,
        double shockStroke)
    {
        var linkage = new Linkage
        {
            Joints = [.. joints],
            Links = [.. links],
            Shock = shock,
            ShockStroke = shockStroke,
        };

        linkage.ResolveJoints();
        return linkage;
    }

    public static Linkage FromSpec(LinkageSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return CreateResolved(
            spec.Joints.Select(joint => new Joint(joint.X, joint.Y) { Name = joint.Name, Type = joint.Type }),
            spec.Links.Select(link => new Link(link.A, link.B)),
            new Link(spec.Shock.A, spec.Shock.B),
            spec.ShockStroke);
    }

    #endregion Initializers

    #region Public methods

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    public Linkage CloneResolved() => FromJson(ToJson());

    public LinkageSpec ToSpec() => new(
        [.. Joints.Select(joint => new JointSpec(joint.Name!, joint.Type, joint.X, joint.Y))],
        [.. Links.Select(link => new LinkSpec(link.A_Name!, link.B_Name!))],
        new LinkSpec(Shock.A_Name!, Shock.B_Name!),
        ShockStroke);

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

    #endregion Public methods
}
