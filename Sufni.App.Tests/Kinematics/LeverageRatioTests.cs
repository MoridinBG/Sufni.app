using Sufni.Kinematics;

namespace Sufni.App.Tests.Kinematics;

public class LeverageRatioTests
{
    [Fact]
    public void FromPoints_ThrowsValidationException_WhenShockTravelDoesNotIncrease()
    {
        var exception = Assert.Throws<LeverageRatioValidationException>(() =>
            LeverageRatio.FromPoints(
            [
                new LeverageRatioPoint(0, 0),
                new LeverageRatioPoint(10, 20),
                new LeverageRatioPoint(10, 25)
            ]));

        Assert.Contains(exception.Errors, error => error.Message.Contains("duplicate values", StringComparison.Ordinal));
    }

    [Fact]
    public void WheelTravelAt_InterpolatesWithinRange_AndClampsOutsideRange()
    {
        var leverageRatio = LeverageRatio.FromPoints(
        [
            new LeverageRatioPoint(0, 0),
            new LeverageRatioPoint(10, 30),
            new LeverageRatioPoint(20, 50)
        ]);

        Assert.Equal(0, leverageRatio.WheelTravelAt(-5));
        Assert.Equal(15, leverageRatio.WheelTravelAt(5));
        Assert.Equal(40, leverageRatio.WheelTravelAt(15));
        Assert.Equal(50, leverageRatio.WheelTravelAt(25));
    }

    [Fact]
    public void DeriveLeverageRatioSamples_ReturnsWheelMidpointsAndSegmentRatios()
    {
        var leverageRatio = LeverageRatio.FromPoints(
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
}