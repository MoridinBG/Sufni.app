using System.Collections.Immutable;
using System.Linq;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.LinkageEditing;

public sealed record LinkageJoint(int Id, string Name, JointType? Type, double X, double Y) : IPoint;
public sealed record LinkageLink(int Id, int? A, int? B);

public sealed record LinkageDraft(ImmutableArray<LinkageJoint> Joints, ImmutableArray<LinkageLink> Links,
    int? ShockId, int NextId, int NextPointNumber)
{
    public static LinkageDraft Empty { get; } = new([], [], null, 1, 1);
    public LinkageJoint? Joint(int? id) => Joints.FirstOrDefault(joint => joint.Id == id);
    public LinkageLink? Link(int? id) => Links.FirstOrDefault(link => link.Id == id);
}

public enum JointEditRule { Modifiable, FixedName, Immutable }

public static class JointEditRules
{
    public static JointEditRule For(LinkageJoint joint) => joint.Type switch
    {
        JointType.FrontWheel or JointType.RearWheel or JointType.BottomBracket or JointType.HeadTube => JointEditRule.Immutable,
        _ when joint.Name is JointNames.ShockEye1 or JointNames.ShockEye2 => JointEditRule.FixedName,
        _ => JointEditRule.Modifiable
    };
}
