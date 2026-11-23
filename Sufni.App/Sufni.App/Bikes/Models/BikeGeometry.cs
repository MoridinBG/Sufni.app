using System;
using System.Linq;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

internal readonly record struct HeadAngleGeometry(
    IPoint? HeadTube1, IPoint? HeadTube2, IPoint? FrontWheel, IPoint? RearWheel,
    double? FrontDiameter, double? RearDiameter, double? Scale);

internal static class BikeGeometry
{
    public static double? HeadAngle(HeadAngleGeometry geometry)
    {
        if (geometry is not { HeadTube1: { } top, HeadTube2: { } bottom,
            FrontWheel: { } front, RearWheel: { } rear, FrontDiameter: { } frontDiameter,
            RearDiameter: { } rearDiameter, Scale: { } scale }) return null;

        return GeometryUtils.CalculateHeadAngle(top, bottom, front, rear, frontDiameter, rearDiameter, scale);
    }

    public static double EffectiveHeadAngle(double storedAngle, RearSuspensionSpec suspension,
        double? frontDiameter, double? rearDiameter)
    {
        if (suspension is not RearSuspensionSpec.Linkage linkage) return storedAngle;
        var joints = linkage.Spec.Joints;
        static JointSpec? ToScreen(JointSpec? joint) => joint is null ? null : joint with { Y = -joint.Y };

        return HeadAngle(new(
            ToScreen(joints.FirstOrDefault(j => j.Name == JointNames.HeadTube1)),
            ToScreen(joints.FirstOrDefault(j => j.Name == JointNames.HeadTube2)),
            ToScreen(joints.FirstOrDefault(j => j.Type == JointType.FrontWheel)),
            ToScreen(joints.FirstOrDefault(j => j.Type == JointType.RearWheel)),
            frontDiameter, rearDiameter, 1)) ?? storedAngle;
    }

    public static double? Chainstay(RearSuspensionSpec rearSuspension)
    {
        if (rearSuspension is not RearSuspensionSpec.Linkage linkage) return null;
        var bottomBracket = linkage.Spec.Joints.FirstOrDefault(joint => joint.Type == JointType.BottomBracket);
        var rearWheel = linkage.Spec.Joints.FirstOrDefault(joint => joint.Type == JointType.RearWheel);
        return bottomBracket is null || rearWheel is null ? null
            : double.Hypot(rearWheel.X - bottomBracket.X, rearWheel.Y - bottomBracket.Y);
    }
}
