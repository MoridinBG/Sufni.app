using Sufni.App.Bikes.Models;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.Models;

public class BikeGeometryTests
{
    public enum IncompleteGeometry { UpperHeadTube, LowerHeadTube, FrontWheel, RearWheel, FrontDiameter, RearDiameter, Scale, CoincidentHeadTube, CoincidentContacts }

    [Theory]
    [InlineData(IncompleteGeometry.UpperHeadTube)]
    [InlineData(IncompleteGeometry.LowerHeadTube)]
    [InlineData(IncompleteGeometry.FrontWheel)]
    [InlineData(IncompleteGeometry.RearWheel)]
    [InlineData(IncompleteGeometry.FrontDiameter)]
    [InlineData(IncompleteGeometry.RearDiameter)]
    [InlineData(IncompleteGeometry.Scale)]
    [InlineData(IncompleteGeometry.CoincidentHeadTube)]
    [InlineData(IncompleteGeometry.CoincidentContacts)]
    public void HeadAngleRequiresADefinedHeadTubeAndGroundLine(IncompleteGeometry missing)
    {
        var points = TestSnapshots.FullSuspensionLinkageSpec(true).Joints;
        JointSpec Point(string name) => points.Single(j => j.Name == name);
        var complete = new HeadAngleGeometry(Point(JointNames.HeadTube1), Point(JointNames.HeadTube2),
            Point(JointNames.FrontWheel), Point(JointNames.RearWheel), 700, 700, 1);
        var geometry = missing switch
        {
            IncompleteGeometry.UpperHeadTube => complete with { HeadTube1 = null },
            IncompleteGeometry.LowerHeadTube => complete with { HeadTube2 = null },
            IncompleteGeometry.FrontWheel => complete with { FrontWheel = null },
            IncompleteGeometry.RearWheel => complete with { RearWheel = null },
            IncompleteGeometry.FrontDiameter => complete with { FrontDiameter = null },
            IncompleteGeometry.RearDiameter => complete with { RearDiameter = null },
            IncompleteGeometry.Scale => complete with { Scale = null },
            IncompleteGeometry.CoincidentHeadTube => complete with { HeadTube1 = complete.HeadTube2 },
            IncompleteGeometry.CoincidentContacts => complete with { FrontWheel = complete.RearWheel },
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };

        Assert.Null(BikeGeometry.HeadAngle(geometry));
    }
}
