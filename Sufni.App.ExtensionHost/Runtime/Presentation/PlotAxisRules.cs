using System;
using ScottPlot;

namespace Sufni.App.ExtensionHost.Runtime.Presentation;

public static class PlotZoomFractions
{
    public const double TimeSeries = 0.01;
    public const double Analysis = 0.10;
}

public static class AxisRangeConstraints
{
    public static (double Minimum, double Maximum) Constrain(
        double minimum,
        double maximum,
        double boundsMinimum,
        double boundsMaximum,
        double minimumSpan)
    {
        if (!double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            !double.IsFinite(boundsMinimum) ||
            !double.IsFinite(boundsMaximum))
        {
            return (minimum, maximum);
        }

        var low = Math.Min(boundsMinimum, boundsMaximum);
        var high = Math.Max(boundsMinimum, boundsMaximum);
        var boundsSpan = high - low;
        if (boundsSpan <= 0)
        {
            return (minimum, maximum);
        }

        var inverted = minimum > maximum;
        var visibleLow = Math.Min(minimum, maximum);
        var visibleHigh = Math.Max(minimum, maximum);
        var visibleSpan = visibleHigh - visibleLow;
        var minAllowedSpan = double.IsFinite(minimumSpan)
            ? Math.Clamp(Math.Abs(minimumSpan), 0, boundsSpan)
            : 0;
        var targetSpan = Math.Clamp(visibleSpan, minAllowedSpan, boundsSpan);
        var center = (visibleLow + visibleHigh) / 2.0;
        var constrainedLow = center - targetSpan / 2.0;
        var constrainedHigh = center + targetSpan / 2.0;

        if (constrainedLow < low)
        {
            constrainedLow = low;
            constrainedHigh = low + targetSpan;
        }

        if (constrainedHigh > high)
        {
            constrainedHigh = high;
            constrainedLow = high - targetSpan;
        }

        return inverted
            ? (constrainedHigh, constrainedLow)
            : (constrainedLow, constrainedHigh);
    }
}

public sealed class BoundedZoomRule(
    IXAxis xAxis,
    IYAxis yAxis,
    double xMin,
    double xMax,
    double yMin,
    double yMax,
    double minSpanFraction = PlotZoomFractions.TimeSeries) : IAxisRule
{
    private readonly double xLow = Math.Min(xMin, xMax);
    private readonly double xHigh = Math.Max(xMin, xMax);
    private readonly double yLow = Math.Min(yMin, yMax);
    private readonly double yHigh = Math.Max(yMin, yMax);
    private readonly double minXSpan = Math.Abs(xMax - xMin) * minSpanFraction;
    private readonly double minYSpan = Math.Abs(yMax - yMin) * minSpanFraction;

    public void Apply(RenderPack rp, bool beforeLayout)
    {
        (xAxis.Min, xAxis.Max) = AxisRangeConstraints.Constrain(
            xAxis.Min,
            xAxis.Max,
            xLow,
            xHigh,
            minXSpan);
        (yAxis.Min, yAxis.Max) = AxisRangeConstraints.Constrain(
            yAxis.Min,
            yAxis.Max,
            yLow,
            yHigh,
            minYSpan);
    }
}
