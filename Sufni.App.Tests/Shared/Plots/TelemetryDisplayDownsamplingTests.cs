
using Sufni.App.Shared.Plots;
namespace Sufni.App.Tests.Shared.Plots;

public class TelemetryDisplayDownsamplingTests
{
    [Fact]
    public void Prepare_ReturnsOriginalSamples_WhenSampleRateIsAtOrBelowCap()
    {
        var samples = new[] { 0.0, 1.0, 2.0, 3.0 };

        var (prepared, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate: 100, maximumDisplayHz: 100);

        Assert.Same(samples, prepared);
        Assert.Equal(0.01, step, 6);
    }

    [Fact]
    public void Prepare_Downsamples_AndAdjustsStep_WhenSampleRateExceedsCap()
    {
        var samples = Enumerable.Range(0, 25).Select(static value => (double)value).ToArray();

        var (prepared, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate: 1000, maximumDisplayHz: 100);

        Assert.Equal([0.0, 10.0, 20.0], prepared);
        Assert.Equal(0.01, step, 6);
    }

    [Fact]
    public void Prepare_UsesCeilingStride_ForNonIntegralRatios()
    {
        var samples = Enumerable.Range(0, 10).Select(static value => (double)value).ToArray();

        var (prepared, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate: 250, maximumDisplayHz: 100);

        Assert.Equal([0.0, 3.0, 6.0, 9.0], prepared);
        Assert.Equal(0.012, step, 6);
    }

    [Fact]
    public void PrepareIrregular_ReturnsOriginalArrays_WhenDisplayCapDoesNotRequireDownsampling()
    {
        var xValues = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        var yValues = new[] { 0.0, 1.0, 2.0, 3.0, 4.0 };

        var (preparedXValues, preparedYValues) = TelemetryDisplayDownsampling.PrepareIrregular(
            xValues,
            yValues,
            maximumDisplayHz: 100);

        Assert.Same(xValues, preparedXValues);
        Assert.Same(yValues, preparedYValues);
    }

    [Fact]
    public void PrepareIrregular_DownsamplesWithCeilingStride_AndPreservesFinalPoint()
    {
        var xValues = Enumerable.Range(0, 11).Select(static value => value / 100.0).ToArray();
        var yValues = Enumerable.Range(0, 11).Select(static value => (double)value).ToArray();

        var (preparedXValues, preparedYValues) = TelemetryDisplayDownsampling.PrepareIrregular(
            xValues,
            yValues,
            maximumDisplayHz: 30);

        Assert.Equal([0.0, 0.04, 0.08, 0.1], preparedXValues);
        Assert.Equal([0.0, 4.0, 8.0, 10.0], preparedYValues);
    }

    [Fact]
    public void PrepareIrregular_ReturnsOriginalArrays_WhenXSpanIsNotPositive()
    {
        var xValues = new[] { 1.0, 0.5, 0.0 };
        var yValues = new[] { 0.0, 1.0, 2.0 };

        var (preparedXValues, preparedYValues) = TelemetryDisplayDownsampling.PrepareIrregular(
            xValues,
            yValues,
            maximumDisplayHz: 30);

        Assert.Same(xValues, preparedXValues);
        Assert.Same(yValues, preparedYValues);
    }
}
