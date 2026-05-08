using Sufni.App.Plots;

namespace Sufni.App.Tests.Plots;

public class AxisRangeConstraintsTests
{
    [Theory]
    [InlineData(-2, 8, 0, 10)]
    [InlineData(2, 12, 0, 10)]
    public void Constrain_WhenFullRangeIsPannedPastBoundary_PreservesFullSpan(
        double minimum,
        double maximum,
        double expectedMinimum,
        double expectedMaximum)
    {
        var constrained = AxisRangeConstraints.Constrain(minimum, maximum, 0, 10, minimumSpan: 0.1);

        Assert.Equal(expectedMinimum, constrained.Minimum);
        Assert.Equal(expectedMaximum, constrained.Maximum);
    }

    [Theory]
    [InlineData(-1, 4, 0, 5)]
    [InlineData(7, 12, 5, 10)]
    public void Constrain_WhenZoomedRangeIsPannedPastBoundary_PreservesVisibleSpan(
        double minimum,
        double maximum,
        double expectedMinimum,
        double expectedMaximum)
    {
        var constrained = AxisRangeConstraints.Constrain(minimum, maximum, 0, 10, minimumSpan: 0.1);

        Assert.Equal(expectedMinimum, constrained.Minimum);
        Assert.Equal(expectedMaximum, constrained.Maximum);
    }

    [Fact]
    public void Constrain_WhenRangeIsSmallerThanMinimumSpan_ExpandsInsideBounds()
    {
        var constrained = AxisRangeConstraints.Constrain(0.2, 0.25, 0, 10, minimumSpan: 1);

        Assert.Equal(0, constrained.Minimum);
        Assert.Equal(1, constrained.Maximum);
    }

    [Fact]
    public void Constrain_WhenRangeIsLargerThanBounds_UsesFullBounds()
    {
        var constrained = AxisRangeConstraints.Constrain(-5, 20, 0, 10, minimumSpan: 0.1);

        Assert.Equal(0, constrained.Minimum);
        Assert.Equal(10, constrained.Maximum);
    }

    [Fact]
    public void Constrain_WhenAxisIsInverted_PreservesInversion()
    {
        var constrained = AxisRangeConstraints.Constrain(8, -2, 0, 10, minimumSpan: 0.1);

        Assert.Equal(10, constrained.Minimum);
        Assert.Equal(0, constrained.Maximum);
    }
}
