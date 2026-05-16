using System.Linq;
using Sufni.App.Models;
using Sufni.App.Plots;

namespace Sufni.App.Tests.Plots;

public class TelemetryDisplaySmoothingTests
{
    [Fact]
    public void Apply_ReturnsOriginalSamples_WhenSmoothingIsOff()
    {
        double[] samples = [1, 9, 1];

        var result = TelemetryDisplaySmoothing.ApplyRegular(samples, PlotSmoothingLevel.Off, samplePeriodSeconds: 0.001);

        Assert.Same(samples, result);
    }

    [Fact]
    public void Apply_UsesConfiguredCenteredMovingAverage_ForRecordedDisplaySamples()
    {
        double[] samples = [1, 2, 3, 4, 5];

        var result = TelemetryDisplaySmoothing.ApplyRegular(samples, PlotSmoothingLevel.Light, samplePeriodSeconds: 0.001);

        Assert.Collection(
            result,
            value => Assert.Equal(3.0, value, 10),
            value => Assert.Equal(3.0, value, 10),
            value => Assert.Equal(3.0, value, 10),
            value => Assert.Equal(3.0, value, 10),
            value => Assert.Equal(3.0, value, 10));
    }

    [Fact]
    public void Apply_UsesSameDurationAtLowerDisplayRates()
    {
        double[] samples = [1, 2, 3, 4, 5];

        var result = TelemetryDisplaySmoothing.ApplyRegular(samples, PlotSmoothingLevel.Light, samplePeriodSeconds: 0.01);

        Assert.Same(samples, result);
    }

    [Fact]
    public void StreamingSmoother_CarriesWindowAcrossBatches()
    {
        var smoother = new TelemetryDisplayStreamingSmoother
        {
            Level = PlotSmoothingLevel.Light,
        };
        double[] buffer = [];

        var firstBatch = smoother.Apply([0.000, 0.001, 0.002], [1, 2, 3], ref buffer).ToArray();
        var secondBatch = smoother.Apply([0.003, 0.004, 0.005], [4, 5, 6], ref buffer).ToArray();

        Assert.Collection(
            firstBatch,
            value => Assert.Equal(1.0, value, 10),
            value => Assert.Equal(1.5, value, 10),
            value => Assert.Equal(2.0, value, 10));
        Assert.Collection(
            secondBatch,
            value => Assert.Equal(2.5, value, 10),
            value => Assert.Equal(3.0, value, 10),
            value => Assert.Equal(3.5, value, 10));
    }

    [Fact]
    public void StreamingSmoother_UsesTimestampsForWindow()
    {
        var smoother = new TelemetryDisplayStreamingSmoother
        {
            Level = PlotSmoothingLevel.Light,
        };
        double[] buffer = [];

        var result = smoother.Apply([0.00, 0.01, 0.02], [1, 2, 3], ref buffer).ToArray();

        Assert.Equal([1, 2, 3], result);
    }
}
