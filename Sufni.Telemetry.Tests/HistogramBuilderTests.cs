using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class HistogramBuilderTests
{
    [Fact]
    public void Digitize_UsesUpperBinForExactInteriorBoundaries()
    {
        double[] bins = [0, 10, 20];
        double[] values = [-1, 0, 5, 10, 15, 20, 21];

        var indexes = HistogramBuilder.Digitize(values, bins);

        int[] expected = [0, 0, 0, 1, 1, 1, 1];
        Assert.Equal(expected, indexes);
        Assert.Equal(expected, values.Select(value => HistogramBuilder.DigitizeValue(value, bins)));
    }

}
