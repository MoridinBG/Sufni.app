using System.Collections.ObjectModel;

namespace Sufni.Kinematics;

public sealed class ResolvedLinkage
{
    private readonly ResolvedJoint[] joints;
    private readonly ResolvedLink[] links;
    private readonly ReadOnlyCollection<ResolvedJoint> jointView;
    private readonly ReadOnlyCollection<ResolvedLink> linkView;

    internal ResolvedLinkage(
        LinkageSpec spec,
        IReadOnlyList<ResolvedJoint> joints,
        IReadOnlyList<ResolvedLink> links,
        ResolvedLink shock)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(shock);

        Spec = spec;
        this.joints = [.. joints];
        this.links = [.. links];
        jointView = Array.AsReadOnly(this.joints);
        linkView = Array.AsReadOnly(this.links);
        Shock = shock;
    }

    public LinkageSpec Spec { get; }

    public IReadOnlyList<ResolvedJoint> Joints => jointView;

    public IReadOnlyList<ResolvedLink> Links => linkView;

    public ResolvedLink Shock { get; }
}

public sealed class ResolvedJoint
{
    internal ResolvedJoint(string name, JointType? type, double x, double y)
    {
        Name = name;
        Type = type;
        X = x;
        Y = y;
    }

    public string Name { get; }

    public JointType? Type { get; }

    public double X { get; set; }

    public double Y { get; set; }

    public bool IsFixed => Type is JointType.Fixed or JointType.BottomBracket;
}

public sealed class ResolvedLink
{
    internal ResolvedLink(string aName, string bName, ResolvedJoint a, ResolvedJoint b)
    {
        ArgumentNullException.ThrowIfNull(aName);
        ArgumentNullException.ThrowIfNull(bName);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        AName = aName;
        BName = bName;
        A = a;
        B = b;
        Length = CalculateLength(a, b);
    }

    public string AName { get; }

    public string BName { get; }

    public ResolvedJoint A { get; }

    public ResolvedJoint B { get; }

    public double Length { get; }

    internal static double CalculateLength(ResolvedJoint a, ResolvedJoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return double.Hypot(dx, dy);
    }
}
