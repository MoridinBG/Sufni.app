using System;
using System.Linq;
using System.Collections.Generic;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

public readonly record struct HeadAngleGeometry(
    IPoint? HeadTube1, IPoint? HeadTube2, IPoint? FrontWheel, IPoint? RearWheel,
    double? FrontDiameter, double? RearDiameter, double? Scale);

internal static class BikeGeometry
{
    public static (T? First, T? Second) HeadTubePair<T>(IEnumerable<T> joints, Func<T, JointType?> type) where T : class
    {
        var pair = joints.Where(joint => type(joint) == JointType.HeadTube).Take(3).ToArray();
        return pair.Length == 2 ? (pair[0], pair[1]) : (null, null);
    }

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
        var (first, second) = HeadTubePair(joints, joint => joint.Type);
        static JointSpec? ToScreen(JointSpec? joint) => joint is null ? null : joint with { Y = -joint.Y };

        return HeadAngle(new(
            ToScreen(first), ToScreen(second),
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
