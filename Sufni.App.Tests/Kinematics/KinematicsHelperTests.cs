using Sufni.Kinematics;

namespace Sufni.App.Tests.Kinematics;

public class KinematicsHelperTests
{
    [Fact]
    public void CalculateTotalDiameterMm_ReturnsBeadSeatDiameterPlusDoubleTireHeight()
    {
        var diameter = EtrtoRimSize.Inch29.CalculateTotalDiameterMm(2.4);

        Assert.Equal(743.92, diameter, 2);
    }

    [Fact]
    public void CalculateGroundRotation_ReturnsNegativeAngle_WhenFrontWheelSitsHigher()
    {
        var result = GroundCalculator.CalculateGroundRotation(
            frontWheelX: 10,
            frontWheelY: 5,
            frontWheelRadius: 1,
            rearWheelX: 0,
            rearWheelY: 0,
            rearWheelRadius: 1);

        Assert.Equal(-26.565, result.angleDegrees, 3);
        Assert.Equal(6, result.groundY, 3);
    }

    [Fact]
    public void CalculateGroundRotation_ReturnsPositiveAngle_WhenRearWheelSitsHigher()
    {
        var result = GroundCalculator.CalculateGroundRotation(
            frontWheelX: 0,
            frontWheelY: 5,
            frontWheelRadius: 1,
            rearWheelX: 10,
            rearWheelY: 0,
            rearWheelRadius: 1);

        Assert.Equal(26.565, result.angleDegrees, 3);
        Assert.Equal(6, result.groundY, 3);
    }

    [Fact]
    public void RotatePoint_RotatesAroundOrigin()
    {
        var result = CoordinateRotation.RotatePoint(1, 0, 0, 0, 90);

        Assert.Equal(0, result.x, 3);
        Assert.Equal(1, result.y, 3);
    }

    [Fact]
    public void GetRotatedBounds_ReturnsQuarterTurnExtents()
    {
        var result = CoordinateRotation.GetRotatedBounds(2, 1, 90);

        Assert.Equal(-1, result.minX, 3);
        Assert.Equal(0, result.minY, 3);
        Assert.Equal(0, result.maxX, 3);
        Assert.Equal(2, result.maxY, 3);
    }

    [Fact]
    public void CalculateDistance_UsesPointCoordinates()
    {
        var distance = GeometryUtils.CalculateDistance(
            new CartesianCoordinate(0, 0),
            new CartesianCoordinate(3, 4));

        Assert.Equal(5, distance, 3);
    }

    [Fact]
    public void CalculatePixelsToMillimetersFromChainstay_UsesWheelToBottomBracketDistance()
    {
        var pixelsToMillimeters = GeometryUtils.CalculatePixelsToMillimetersFromChainstay(
            chainstayMillimeters: 450,
            rearWheel: new CartesianCoordinate(0, 0),
            bottomBracket: new CartesianCoordinate(3, 4));

        Assert.Equal(90, pixelsToMillimeters);
    }

    [Fact]
    public void CalculatePixelsToMillimetersFromChainstay_ReturnsNullForDegenerateCalibration()
    {
        var pixelsToMillimeters = GeometryUtils.CalculatePixelsToMillimetersFromChainstay(
            chainstayMillimeters: 450,
            rearWheel: new CartesianCoordinate(0, 0),
            bottomBracket: new CartesianCoordinate(0, 0));

        Assert.Null(pixelsToMillimeters);
    }

    [Fact]
    public void CalculatePixelsToMillimetersFromChainstay_ReturnsNullForMissingChainstay()
    {
        var pixelsToMillimeters = GeometryUtils.CalculatePixelsToMillimetersFromChainstay(
            chainstayMillimeters: null,
            rearWheel: new CartesianCoordinate(0, 0),
            bottomBracket: new CartesianCoordinate(3, 4));

        Assert.Null(pixelsToMillimeters);
    }

    [Fact]
    public void CalculateHeadAngle_ReturnsRoundedAngleAgainstWheelContactGround()
    {
        var headAngle = GeometryUtils.CalculateHeadAngle(
            headTube1: new CartesianCoordinate(0, -10),
            headTube2: new CartesianCoordinate(0, 0),
            frontWheel: new CartesianCoordinate(10, 0),
            rearWheel: new CartesianCoordinate(0, 0),
            frontWheelDiameter: 2,
            rearWheelDiameter: 2,
            pixelsToMillimeters: 1);

        Assert.Equal(90, headAngle);
    }

    [Fact]
    public void CalculateHeadAngle_ReturnsNullForDegenerateGeometry()
    {
        var headAngle = GeometryUtils.CalculateHeadAngle(
            headTube1: new CartesianCoordinate(0, 0),
            headTube2: new CartesianCoordinate(0, 0),
            frontWheel: new CartesianCoordinate(10, 0),
            rearWheel: new CartesianCoordinate(0, 0),
            frontWheelDiameter: 2,
            rearWheelDiameter: 2,
            pixelsToMillimeters: 1);

        Assert.Null(headAngle);
    }

    [Fact]
    public void CalculateAngleAtPoint_WithCoincidentPoints_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GeometryUtils.CalculateAngleAtPoint(
                centralX: 0,
                centralY: 0,
                adjacent1X: 0,
                adjacent1Y: 0,
                adjacent2X: 1,
                adjacent2Y: 0));
    }
}
