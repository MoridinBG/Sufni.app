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
    public void FromJson_ReadsPersistedPointNamesAndOrder()
    {
        const string json = """{"points":[{"shock_travel_mm":0,"wheel_travel_mm":0},{"shock_travel_mm":10,"wheel_travel_mm":30},{"shock_travel_mm":20,"wheel_travel_mm":50}]}""";

        var parsed = LeverageRatioSpec.FromJson(json);

        Assert.NotNull(parsed);
        Assert.Equal([new LeverageRatioPoint(0, 0), new LeverageRatioPoint(10, 30), new LeverageRatioPoint(20, 50)], parsed.Points);
        Assert.Equal(json, parsed.ToJson());
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

}
