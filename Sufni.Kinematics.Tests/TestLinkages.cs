using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

internal static class TestLinkages
{
    public static LinkageSpec FullSuspensionLinkageSpec(bool includeHeadTubeJoints = false)
    {

        List<JointSpec> joints =
        [
            new(JointNames.BottomBracket, JointType.BottomBracket, 0, 0),
            new(JointNames.RearWheel, JointType.RearWheel, 4, 0),
            new(JointNames.FrontWheel, JointType.FrontWheel, 12, 1),
            new(JointNames.ShockEye1, JointType.Floating, 4, 3),
            new(JointNames.ShockEye2, JointType.Fixed, 0, 3)
        ];
        if (includeHeadTubeJoints)
        {
            joints.Add(new JointSpec(JointNames.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new JointSpec(JointNames.HeadTube2, JointType.HeadTube, 9, 5));
        }

        return new LinkageSpec(
            joints,
            [
                new LinkSpec(JointNames.BottomBracket, JointNames.RearWheel),
                new LinkSpec(JointNames.RearWheel, JointNames.ShockEye1),
            ],
            new LinkSpec(JointNames.ShockEye1, JointNames.ShockEye2),
            0.5);
    }
}
