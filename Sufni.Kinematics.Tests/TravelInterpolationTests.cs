using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class TravelInterpolationTests
{
    [Fact]
    public void WheelTravelAt_InterpolatesWithinRange_AndClampsOutsideRange()
    {
        var curve = new CoordinateList([0, 10, 20], [0, 30, 50]);

        Assert.Equal(0, TravelInterpolation.WheelTravelAt(curve, -5));
        Assert.Equal(15, TravelInterpolation.WheelTravelAt(curve, 5));
        Assert.Equal(40, TravelInterpolation.WheelTravelAt(curve, 15));
        Assert.Equal(50, TravelInterpolation.WheelTravelAt(curve, 25));
    }
}
