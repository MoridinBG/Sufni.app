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
    public void ApplyRegular_UsesZeroPhaseLowPass_ForRecordedDisplaySamples()
    {
        double[] samples = [0, 0, 10, 10, 10];

        var result = TelemetryDisplaySmoothing.ApplyRegular(samples, PlotSmoothingLevel.Light, samplePeriodSeconds: 0.05);

        Assert.NotSame(samples, result);
        Assert.InRange(result[0], 0.9, 1.1);
        Assert.InRange(result[2], 7.2, 7.4);
        Assert.InRange(result[4], 9.4, 9.6);
    }

    [Fact]
    public void ApplyRegular_SmoothsAtLowerDisplayRates()
    {
        double[] samples = [0, 0, 10, 10, 10];

        var result = TelemetryDisplaySmoothing.ApplyRegular(samples, PlotSmoothingLevel.Light, samplePeriodSeconds: 0.01);

        Assert.NotSame(samples, result);
        Assert.InRange(result[0], 2.5, 2.7);
        Assert.InRange(result[4], 4.4, 4.6);
    }

    [Fact]
    public void ApplyRegular_UsesComparableTimeResponseAcrossSampleRates()
    {
        var fastSamples = CreateStepSamples(samplePeriodSeconds: 0.001);
        var slowSamples = CreateStepSamples(samplePeriodSeconds: 0.01);

        var fastResult = TelemetryDisplaySmoothing.ApplyRegular(fastSamples, PlotSmoothingLevel.Light, samplePeriodSeconds: 0.001);
        var slowResult = TelemetryDisplaySmoothing.ApplyRegular(slowSamples, PlotSmoothingLevel.Light, samplePeriodSeconds: 0.01);

        Assert.InRange(Math.Abs(fastResult[550] - slowResult[55]), 0, 0.05);
    }

    [Fact]
    public void ApplyIrregular_UsesTimestampDeltas()
    {
        double[] xValues = [0.0, 0.01, 0.02, 0.10];
        double[] samples = [0, 10, 10, 10];

        var result = TelemetryDisplaySmoothing.ApplyIrregular(xValues, samples, PlotSmoothingLevel.Light);

        Assert.NotSame(samples, result);
        Assert.InRange(result[1], 3.8, 4.0);
        Assert.InRange(result[3], 8.5, 8.7);
    }

    [Fact]
    public void StreamingSmoother_CarriesTimeConstantAcrossBatches()
    {
        var smoother = new TelemetryDisplayStreamingSmoother
        {
            Level = PlotSmoothingLevel.Light,
        };
        double[] buffer = [];

        var firstBatch = smoother.Apply([0.00, 0.01, 0.02], [0, 10, 10], ref buffer).ToArray();
        var secondBatch = smoother.Apply([0.10], [10], ref buffer).ToArray();

        Assert.Collection(
            firstBatch,
            value => Assert.Equal(0.0, value, 10),
            value => Assert.InRange(value, 1.8, 1.9),
            value => Assert.InRange(value, 3.2, 3.4));
        Assert.Collection(
            secondBatch,
            value => Assert.InRange(value, 8.6, 8.7));
    }

    [Fact]
    public void StreamingSmoother_UsesElapsedTimeForResponse()
    {
        var shortDeltaSmoother = new TelemetryDisplayStreamingSmoother
        {
            Level = PlotSmoothingLevel.Light,
        };
        var longDeltaSmoother = new TelemetryDisplayStreamingSmoother
        {
            Level = PlotSmoothingLevel.Light,
        };
        double[] buffer = [];

        var shortDeltaResult = shortDeltaSmoother.Apply([0.00, 0.01], [0, 10], ref buffer).ToArray();
        var longDeltaResult = longDeltaSmoother.Apply([0.00, 0.10], [0, 10], ref buffer).ToArray();

        Assert.InRange(shortDeltaResult[1], 1.8, 1.9);
        Assert.InRange(longDeltaResult[1], 8.6, 8.7);
    }

    private static double[] CreateStepSamples(double samplePeriodSeconds)
    {
        var sampleCount = (int)Math.Round(1.0 / samplePeriodSeconds) + 1;
        return Enumerable.Range(0, sampleCount)
            .Select(index => index * samplePeriodSeconds >= 0.5 ? 1.0 : 0.0)
            .ToArray();
    }
}
