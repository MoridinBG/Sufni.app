using System.Linq;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Plots;
namespace Sufni.App.Tests.Shared.Plots;

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
    public void ApplyRegular_OneBufferMatchesTwoBufferOutputBitForBit()
    {
        double[] samples = [0, 1, 10, -5, double.NaN, 3, double.PositiveInfinity, 4, 4];

        var expected = ApplyRegularTwoBuffer(samples, PlotSmoothingLevel.Strong, samplePeriodSeconds: 0.003);
        var actual = TelemetryDisplaySmoothing.ApplyRegular(samples, PlotSmoothingLevel.Strong, samplePeriodSeconds: 0.003);

        AssertBitExact(expected, actual);
    }

    [Fact]
    public void ApplyIrregular_OneBufferMatchesTwoBufferOutputBitForBitAcrossTimeResets()
    {
        double[] xValues = [0, 0.01, 0.02, 0.015, 0.03, double.NaN, 0.05, 0.10];
        double[] samples = [0, 10, 5, 7, double.NegativeInfinity, 3, 8, 9];

        var expected = ApplyIrregularTwoBuffer(xValues, samples, PlotSmoothingLevel.Light);
        var actual = TelemetryDisplaySmoothing.ApplyIrregular(xValues, samples, PlotSmoothingLevel.Light);

        AssertBitExact(expected, actual);
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

    private static double[] ApplyRegularTwoBuffer(
        double[] samples,
        PlotSmoothingLevel level,
        double samplePeriodSeconds)
    {
        var timeConstantSeconds = TelemetryDisplaySmoothing.GetTimeConstantMilliseconds(level) / 1000.0;
        if (timeConstantSeconds <= 0 ||
            samples.Length <= 2 ||
            !double.IsFinite(samplePeriodSeconds) ||
            samplePeriodSeconds <= 0)
        {
            return samples;
        }

        var forward = new double[samples.Length];
        var output = new double[samples.Length];
        ApplyReferencePass(
            samples,
            forward,
            Enumerable.Range(0, samples.Length),
            _ => samplePeriodSeconds,
            timeConstantSeconds);
        ApplyReferencePass(
            forward,
            output,
            Enumerable.Range(0, samples.Length).Reverse(),
            _ => samplePeriodSeconds,
            timeConstantSeconds);
        return output;
    }

    private static double[] ApplyIrregularTwoBuffer(
        double[] xValues,
        double[] samples,
        PlotSmoothingLevel level)
    {
        var timeConstantSeconds = TelemetryDisplaySmoothing.GetTimeConstantMilliseconds(level) / 1000.0;
        if (timeConstantSeconds <= 0 || samples.Length <= 2 || xValues.Length != samples.Length)
        {
            return samples;
        }

        var forward = new double[samples.Length];
        var output = new double[samples.Length];
        ApplyReferencePass(
            samples,
            forward,
            Enumerable.Range(0, samples.Length),
            index => index == 0 ? 0 : xValues[index] - xValues[index - 1],
            timeConstantSeconds,
            xValues);
        ApplyReferencePass(
            forward,
            output,
            Enumerable.Range(0, samples.Length).Reverse(),
            index => index == samples.Length - 1 ? 0 : xValues[index + 1] - xValues[index],
            timeConstantSeconds,
            xValues);
        return output;
    }

    private static void ApplyReferencePass(
        IReadOnlyList<double> samples,
        double[] output,
        IEnumerable<int> indexes,
        Func<int, double> getDeltaSeconds,
        double timeConstantSeconds,
        IReadOnlyList<double>? xValues = null)
    {
        var smoothed = 0.0;
        var hasSmoothed = false;
        foreach (var index in indexes)
        {
            var value = samples[index];
            if (!double.IsFinite(value) ||
                xValues?[index] is { } timestamp && !double.IsFinite(timestamp))
            {
                hasSmoothed = false;
                output[index] = value;
                continue;
            }

            var deltaSeconds = getDeltaSeconds(index);
            if (!hasSmoothed || deltaSeconds < 0)
            {
                smoothed = value;
                hasSmoothed = true;
                output[index] = value;
                continue;
            }

            var alpha = TelemetryDisplaySmoothing.CalculateAlpha(deltaSeconds, timeConstantSeconds);
            smoothed += alpha * (value - smoothed);
            output[index] = smoothed;
        }
    }

    private static void AssertBitExact(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(expected[index]),
                BitConverter.DoubleToInt64Bits(actual[index]));
        }
    }
}
