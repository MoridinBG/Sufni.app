using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class HistogramBuilderTests
{
    [Fact]
    public void Linspace_CreatesInclusiveBins()
    {
        var bins = HistogramBuilder.Linspace(0, 20, 3);

        Assert.Equal([0, 10, 20], bins);
    }

    [Fact]
    public void Digitize_UsesUpperBinForExactInteriorBoundaries()
    {
        double[] bins = [0, 10, 20];
        double[] values = [-1, 0, 5, 10, 15, 20, 21];

        var indexes = HistogramBuilder.Digitize(values, bins);

        Assert.Equal([0, 0, 0, 1, 1, 1, 1], indexes);
    }

    [Fact]
    public void DigitizeValue_UsesLowerBinForExactInteriorBoundaries()
    {
        double[] bins = [0, 10, 20];

        Assert.Equal(0, HistogramBuilder.DigitizeValue(10, bins));
        Assert.Equal(1, HistogramBuilder.DigitizeValue(20, bins));
    }

    [Fact]
    public void DigitizeVelocity_CentersZeroWithinVelocityBin()
    {
        double[] velocity = [-12, 0, 12];

        var result = HistogramBuilder.DigitizeVelocity(velocity, 10);

        Assert.Equal([-25, -15, -5, 5, 15, 25], result.Bins);
        Assert.Equal([1, 2, 3], result.Values);
    }
}
