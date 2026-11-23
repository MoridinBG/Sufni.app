using System;
using System.Linq;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.LinkageEditing;

public static class LinkageIntentReducers
{
    public static LinkageDraft Seed()
    {
        LinkageJoint[] joints =
        [
            new(1, JointNames.FrontWheel, JointType.FrontWheel, 100, 150),
            new(2, JointNames.BottomBracket, JointType.BottomBracket, 100, 200),
            new(3, JointNames.RearWheel, JointType.RearWheel, 100, 100),
            new(4, JointNames.HeadTube1, JointType.HeadTube, 100, 50),
            new(5, JointNames.HeadTube2, JointType.HeadTube, 100, 120),
            new(6, JointNames.ShockEye1, JointType.Floating, 100, 250),
            new(7, JointNames.ShockEye2, JointType.Floating, 100, 300)
        ];
        return new([.. joints], [new(8, 6, 7)], 8, 9, 1);
    }

    public static LinkageDraft AddJoint(LinkageDraft draft, double x = 100, double y = 100)
    {
        var number = draft.NextPointNumber;
        while (draft.Joints.Any(joint => joint.Name == $"Point{number}")) number++;
        var joint = new LinkageJoint(draft.NextId, $"Point{number}", JointType.Floating, x, y);
        return draft with { Joints = draft.Joints.Add(joint), NextId = joint.Id + 1, NextPointNumber = number + 1 };
    }

    public static LinkageDraft AddLink(LinkageDraft draft) => draft with
    {
        Links = draft.Links.Add(new(draft.NextId, null, null)), NextId = draft.NextId + 1
    };

    public static LinkageDraft Move(LinkageDraft draft, int id, double x, double y) =>
        ChangeJoint(draft, id, joint => joint with { X = x, Y = y });

    public static LinkageDraft Rename(LinkageDraft draft, int id, string name) =>
        ChangeJoint(draft, id, joint => JointEditRules.For(joint) == JointEditRule.Modifiable ? joint with { Name = name } : joint);

    public static LinkageDraft SetType(LinkageDraft draft, int id, JointType? type) =>
        ChangeJoint(draft, id, joint =>
        {
            if (joint.Type == type || JointEditRules.For(joint) == JointEditRule.Immutable) return joint;
            if (type != JointType.HeadTube) return joint with { Type = type };
            var name = JointEditRules.AvailableHeadTubeName(draft, joint);
            return name is null ? joint : joint with { Name = name, Type = type };
        });

    private static LinkageDraft ChangeJoint(LinkageDraft draft, int id, Func<LinkageJoint, LinkageJoint> change) =>
        draft.Joint(id) is { } joint ? draft with { Joints = draft.Joints.Replace(joint, change(joint)) } : draft;

    public static LinkageDraft SetLinkEnd(LinkageDraft draft, int id, bool first, int? jointId)
    {
        if (id == draft.ShockId || draft.Link(id) is not { } link || (jointId.HasValue && draft.Joint(jointId) is null))
            return draft;
        var updated = first ? link with { A = jointId } : link with { B = jointId };
        return draft with { Links = draft.Links.Replace(link, updated) };
    }

    public static bool CanDelete(LinkageDraft draft, int? id) =>
        draft.Link(id) is { } link ? link.Id != draft.ShockId :
        draft.Joint(id) is { } joint && JointEditRules.For(joint) == JointEditRule.Modifiable &&
        !(draft.Link(draft.ShockId) is { } shock && (shock.A == id || shock.B == id));

    public static LinkageDraft Delete(LinkageDraft draft, int? id)
    {
        if (!CanDelete(draft, id)) return draft;
        return draft with
        {
            Joints = draft.Joints.RemoveAll(joint => joint.Id == id),
            Links = draft.Links.RemoveAll(link => link.Id == id || link.A == id || link.B == id)
        };
    }

    public static LinkageDraft Rotate(LinkageDraft draft, double degrees) => draft with
    {
        Joints = [.. draft.Joints.Select(joint =>
        {
            var (x, y) = CoordinateRotation.RotatePoint(joint.X, joint.Y, 0, 0, degrees);
            return joint with { X = x, Y = y };
        })]
    };
}
