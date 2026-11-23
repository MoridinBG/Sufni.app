using System;
using System.Linq;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Shared.Common;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public static class BikeAuthoredDraftComparison
{
    public static bool HasChanges(BikeEditorInputs inputs, BikeDraft baseline, double? headAngle, RearSuspensionSpec spec)
    {
        if (!MathUtils.AreEqual(headAngle, baseline.HeadAngle) || !MathUtils.AreEqual(inputs.ForkStroke, baseline.ForkStroke) ||
            !MathUtils.AreEqual(inputs.ShockStroke, baseline.ShockStroke) || inputs.Mode != baseline.RearSuspension.Kind ||
            !SameWheel(inputs.FrontWheel, baseline.FrontWheel) || !SameWheel(inputs.RearWheel, baseline.RearWheel)) return true;

        if (inputs.Mode == RearSuspensionKind.LeverageRatio)
            return inputs.Curve != (baseline.RearSuspension as RearSuspensionSpec.LeverageRatio)?.Spec;
        if (inputs.Mode != RearSuspensionKind.Linkage) return false;

        var bytes = inputs.Image?.Bytes ?? ReadOnlyMemory<byte>.Empty;
        if (!MathUtils.AreEqual(inputs.Chainstay, baseline.Chainstay) || !MathUtils.AreEqual(inputs.Rotation, baseline.ImageRotationDegrees) ||
            !bytes.Span.SequenceEqual(baseline.ImageBytesMemory.Span)) return true;

        if (baseline.RearSuspension is not RearSuspensionSpec.Linkage original)
            return !inputs.Linkage.Joints.IsEmpty || !inputs.Linkage.Links.IsEmpty;
        if (spec is not RearSuspensionSpec.Linkage current) return inputs.Image is not null;
        return !Equivalent(current.Spec, original.Spec);
    }

    private static bool SameWheel(WheelSpec? left, WheelSpec? right) => left?.RimSize == right?.RimSize &&
        MathUtils.AreEqual(left?.TireWidth, right?.TireWidth) && MathUtils.AreEqual(left?.DiameterMm, right?.DiameterMm);

    private static bool Equivalent(LinkageSpec current, LinkageSpec original)
    {
        if (current.Shock != original.Shock || current.Joints.Count != original.Joints.Count ||
            current.Links.Count != original.Links.Count) return false;
        var remaining = original.Joints.ToList();
        foreach (var joint in current.Joints)
        {
            var index = remaining.FindIndex(candidate => candidate.Name == joint.Name && candidate.Type == joint.Type &&
                MathUtils.AreEqual(candidate.X, joint.X) && MathUtils.AreEqual(candidate.Y, joint.Y));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        var links = original.Links.ToList();
        return current.Links.All(links.Remove);
    }
}
