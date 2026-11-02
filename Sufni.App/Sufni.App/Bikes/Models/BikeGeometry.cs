using System;
using System.Linq;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

internal static class BikeGeometry
{
    public static double? Chainstay(RearSuspensionSpec rearSuspension)
    {
        if (rearSuspension is not RearSuspensionSpec.Linkage linkage) return null;
        var bottomBracket = linkage.Spec.Joints.FirstOrDefault(joint => joint.Type == JointType.BottomBracket);
        var rearWheel = linkage.Spec.Joints.FirstOrDefault(joint => joint.Type == JointType.RearWheel);
        return bottomBracket is null || rearWheel is null ? null
            : double.Hypot(rearWheel.X - bottomBracket.X, rearWheel.Y - bottomBracket.Y);
    }
}
