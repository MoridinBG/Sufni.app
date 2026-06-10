using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

internal static class TestLinkages
{
    public static Linkage FullSuspensionLinkage(bool includeHeadTubeJoints = false)
    {
        var mapping = new JointNameMapping();
        var bottomBracket = new Joint(mapping.BottomBracket, JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint(mapping.RearWheel, JointType.RearWheel, 4, 0);
        var frontWheel = new Joint(mapping.FrontWheel, JointType.FrontWheel, 12, 1);
        var shockEye1 = new Joint(mapping.ShockEye1, JointType.Floating, 4, 3);
        var shockEye2 = new Joint(mapping.ShockEye2, JointType.Fixed, 0, 3);

        List<Joint> joints = [bottomBracket, rearWheel, frontWheel, shockEye1, shockEye2];
        if (includeHeadTubeJoints)
        {
            joints.Add(new Joint(mapping.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new Joint(mapping.HeadTube2, JointType.HeadTube, 9, 5));
        }

        return Linkage.CreateResolved(
            joints,
            [
                new Link(bottomBracket, rearWheel),
                new Link(rearWheel, shockEye1),
            ],
            new Link(shockEye1, shockEye2),
            0.5);
    }
}
