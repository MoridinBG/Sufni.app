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
    public static string? AvailableHeadTubeName(LinkageDraft draft, LinkageJoint candidate)
    {
        if (For(candidate) != JointEditRule.Modifiable) return null;
        var existing = draft.Joints.Where(joint => joint.Type == JointType.HeadTube).Take(2).ToArray();
        if (existing.Length == 2) return null;
        var preferSecond = existing.Length == 1 && existing[0].Name != JointNames.HeadTube2;
        var preferred = preferSecond ? JointNames.HeadTube2 : JointNames.HeadTube1;
        var alternate = preferSecond ? JointNames.HeadTube1 : JointNames.HeadTube2;
        bool Available(string name) => !draft.Joints.Any(joint => joint.Id != candidate.Id && joint.Name == name);
        if (Available(preferred)) return preferred;
        return Available(alternate) ? alternate : null;
    }

    public static JointEditRule For(LinkageJoint joint) => joint.Type switch
    {
        JointType.FrontWheel or JointType.RearWheel or JointType.BottomBracket or JointType.HeadTube => JointEditRule.Immutable,
        _ when joint.Name is JointNames.ShockEye1 or JointNames.ShockEye2 => JointEditRule.FixedName,
        _ => JointEditRule.Modifiable
    };
}
