using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

internal static class TestLinkages
{
    public static LinkageSpec FullSuspensionLinkageSpec(bool includeHeadTubeJoints = false)
    {
        var mapping = new JointNameMapping();

        List<JointSpec> joints =
        [
            new(mapping.BottomBracket, JointType.BottomBracket, 0, 0),
            new(mapping.RearWheel, JointType.RearWheel, 4, 0),
            new(mapping.FrontWheel, JointType.FrontWheel, 12, 1),
            new(mapping.ShockEye1, JointType.Floating, 4, 3),
            new(mapping.ShockEye2, JointType.Fixed, 0, 3)
        ];
        if (includeHeadTubeJoints)
        {
            joints.Add(new JointSpec(mapping.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new JointSpec(mapping.HeadTube2, JointType.HeadTube, 9, 5));
        }

        return new LinkageSpec(
            joints,
            [
                new LinkSpec(mapping.BottomBracket, mapping.RearWheel),
                new LinkSpec(mapping.RearWheel, mapping.ShockEye1),
            ],
            new LinkSpec(mapping.ShockEye1, mapping.ShockEye2),
            0.5);
    }
}
