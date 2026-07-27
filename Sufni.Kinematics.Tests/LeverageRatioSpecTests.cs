using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class LeverageRatioSpecTests
{
    [Fact]
    public void FromPoints_ThrowsValidationException_WhenShockTravelDoesNotIncrease()
    {
        var exception = Assert.Throws<LeverageRatioValidationException>(() =>
            LeverageRatioSpec.FromPoints(
            [
                new LeverageRatioPoint(0, 0),
                new LeverageRatioPoint(10, 20),
                new LeverageRatioPoint(10, 25)
            ]));

        Assert.Contains(exception.Errors, error => error.Code == LeverageRatioValidationErrorCode.DuplicateShockTravel);
    }

    [Fact]
    public void FromPoints_CopiesPoints()
    {
        LeverageRatioPoint[] points =
        [
            new(0, 0),
            new(10, 25)
        ];

        var leverageRatio = LeverageRatioSpec.FromPoints(points);
        points[1] = new LeverageRatioPoint(10, 40);

        Assert.Equal(25, leverageRatio.Points[1].WheelTravelMm);
    }

    [Fact]
    public void FromJson_RoundTripsPointOrder()
    {
        var leverageRatio = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0, 0),
            new LeverageRatioPoint(10, 30),
            new LeverageRatioPoint(20, 50)
        ]);

        var parsed = LeverageRatioSpec.FromJson(leverageRatio.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(leverageRatio.Points, parsed.Points);
    }

    [Fact]
    public void StructuralEquality_UsesExactPointOrderAndDoubleBits()
    {
        var leverageRatio = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0.0, 0),
            new LeverageRatioPoint(10, 30)
        ]);
        var same = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0.0, 0),
            new LeverageRatioPoint(10, 30)
        ]);
        var negativeZero = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(-0.0, 0),
            new LeverageRatioPoint(10, 30)
        ]);

        Assert.Equal(leverageRatio, same);
        Assert.Equal(leverageRatio.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(leverageRatio, negativeZero);
    }

    [Fact]
    public void DeriveLeverageRatioSamples_ReturnsWheelMidpointsAndSegmentRatios()
    {
        var leverageRatio = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0, 0),
            new LeverageRatioPoint(10, 30),
            new LeverageRatioPoint(20, 50)
        ]);

        var samples = leverageRatio.DeriveLeverageRatioSamples();

        Assert.Collection(
            samples,
            sample =>
            {
                Assert.Equal(15, sample.WheelTravelMm);
                Assert.Equal(3, sample.Ratio);
            },
            sample =>
            {
                Assert.Equal(40, sample.WheelTravelMm);
                Assert.Equal(2, sample.Ratio);
            });
    }

    [Fact]
    public void DeriveLeverageRatioData_ReturnsWheelMidpointsAndSegmentRatios()
    {
        var leverageRatio = LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0, 0),
            new LeverageRatioPoint(10, 30),
            new LeverageRatioPoint(20, 50)
        ]);

        var data = leverageRatio.DeriveLeverageRatioData();

        Assert.Collection(
            data.X,
            wheelTravel => Assert.Equal(15, wheelTravel),
            wheelTravel => Assert.Equal(40, wheelTravel));
        Assert.Collection(
            data.Y,
            ratio => Assert.Equal(3, ratio),
            ratio => Assert.Equal(2, ratio));
    }
}
