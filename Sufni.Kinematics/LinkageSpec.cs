using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public sealed class LinkageSpec : IEquatable<LinkageSpec>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly JointSpec[] joints;
    private readonly LinkSpec[] links;
    private readonly ReadOnlyCollection<JointSpec> jointView;
    private readonly ReadOnlyCollection<LinkSpec> linkView;

    [JsonConstructor]
    public LinkageSpec(
        IReadOnlyList<JointSpec> joints,
        IReadOnlyList<LinkSpec> links,
        LinkSpec shock,
        double shockStroke)
    {
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(shock);

        this.joints = [.. joints];
        this.links = [.. links];
        jointView = Array.AsReadOnly(this.joints);
        linkView = Array.AsReadOnly(this.links);
        Shock = shock;
        ShockStroke = shockStroke;
    }

    [JsonPropertyName("joints")]
    public IReadOnlyList<JointSpec> Joints => jointView;

    [JsonPropertyName("links")]
    public IReadOnlyList<LinkSpec> Links => linkView;

    [JsonPropertyName("shock")]
    public LinkSpec Shock { get; }

    [JsonPropertyName("shock_stroke")]
    public double ShockStroke { get; }

    public static LinkageSpec FromJson(string json)
    {
        return JsonSerializer.Deserialize<LinkageSpec>(json, JsonOptions)
               ?? throw new JsonException("Linkage JSON did not contain a linkage spec.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public LinkageSpec WithShockStroke(double shockStroke) => new(joints, links, Shock, shockStroke);

    public bool Equals(LinkageSpec? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               DoubleBitsEqual(ShockStroke, other.ShockStroke) &&
               Shock == other.Shock &&
               JointsEqual(joints, other.joints) &&
               LinksEqual(links, other.links);
    }

    public override bool Equals(object? obj) => obj is LinkageSpec other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var joint in joints)
        {
            AddJoint(ref hash, joint);
        }

        foreach (var link in links)
        {
            hash.Add(link);
        }

        hash.Add(Shock);
        hash.Add(DoubleBits(ShockStroke));
        return hash.ToHashCode();
    }

    public static bool operator ==(LinkageSpec? left, LinkageSpec? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(LinkageSpec? left, LinkageSpec? right) => !(left == right);

    private static bool JointsEqual(IReadOnlyList<JointSpec> left, IReadOnlyList<JointSpec> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftJoint = left[index];
            var rightJoint = right[index];
            if (leftJoint.Name != rightJoint.Name ||
                leftJoint.Type != rightJoint.Type ||
                !DoubleBitsEqual(leftJoint.X, rightJoint.X) ||
                !DoubleBitsEqual(leftJoint.Y, rightJoint.Y))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LinksEqual(IReadOnlyList<LinkSpec> left, IReadOnlyList<LinkSpec> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void AddJoint(ref HashCode hash, JointSpec joint)
    {
        hash.Add(joint.Name);
        hash.Add(joint.Type);
        hash.Add(DoubleBits(joint.X));
        hash.Add(DoubleBits(joint.Y));
    }

    private static long DoubleBits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static bool DoubleBitsEqual(double left, double right) => DoubleBits(left) == DoubleBits(right);
}
