using System.Linq;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.LinkageEditing;

public static class LinkageSpecMapping
{
    public static LinkageDraft FromSpec(LinkageSpec? spec, double? imageHeight, double? scale)
    {
        if (spec is null || imageHeight is not { } height || scale is not > 0 || !double.IsFinite(scale.Value))
            return LinkageDraft.Empty;

        var nextId = 1;
        var joints = spec.Joints.Select(joint => new LinkageJoint(nextId++, joint.Name, joint.Type,
            joint.X / scale.Value, height - joint.Y / scale.Value)).ToArray();
        int? Resolve(string? name) => joints.FirstOrDefault(joint => joint.Name == name)?.Id;
        var links = spec.Links.Append(spec.Shock).Select(link => new LinkageLink(nextId++, Resolve(link.A), Resolve(link.B))).ToArray();
        return new LinkageDraft([.. joints], [.. links], links[^1].Id, nextId, 1);
    }

    public static LinkageSpec? ToSpec(LinkageDraft draft, double? imageHeight, double? scale, double? shockStroke)
    {
        if (imageHeight is not { } height || scale is not > 0 || !double.IsFinite(scale.Value) ||
            shockStroke is not { } stroke || draft.Link(draft.ShockId) is not { } shock ||
            draft.Links.Any(link => draft.Joint(link.A) is null || draft.Joint(link.B) is null)) return null;

        LinkSpec Resolve(LinkageLink link) => new(draft.Joint(link.A)!.Name, draft.Joint(link.B)!.Name);
        return new LinkageSpec(
            [.. draft.Joints.Select(joint => new JointSpec(joint.Name, joint.Type, joint.X * scale.Value,
                (height - joint.Y) * scale.Value))],
            [.. draft.Links.Where(link => link.Id != shock.Id).Select(Resolve)], Resolve(shock), stroke);
    }
}
