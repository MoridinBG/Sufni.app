using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

internal static class ZoomFractions
{
    public const double TimeSeries = 0.01;
    public const double Statistics = 0.10;
}

internal class LockedVerticalSoftLockedHorizontalRule(IXAxis xAxis, IYAxis yAxis, double xMin, double xMax, double yMin, double yMax, double minSpanFraction = ZoomFractions.TimeSeries) : IAxisRule
{
    private readonly double minXSpan = (xMax - xMin) * minSpanFraction;

    public void Apply(RenderPack rp, bool beforeLayout)
    {
        if (xAxis.Min < xMin) xAxis.Min = xMin;
        if (xAxis.Max > xMax) xAxis.Max = xMax;

        var xSpan = xAxis.Max - xAxis.Min;
        if (xSpan < minXSpan)
        {
            var center = (xAxis.Min + xAxis.Max) / 2.0;
            xAxis.Min = center - minXSpan / 2.0;
            xAxis.Max = center + minXSpan / 2.0;
        }

        yAxis.Range.Set(yMin, yMax);
    }
}

internal class BoundedZoomRule(IXAxis xAxis, IYAxis yAxis, double xMin, double xMax, double yMin, double yMax, double minSpanFraction = ZoomFractions.TimeSeries) : IAxisRule
{
    private readonly double xLow = Math.Min(xMin, xMax);
    private readonly double xHigh = Math.Max(xMin, xMax);
    private readonly double yLow = Math.Min(yMin, yMax);
    private readonly double yHigh = Math.Max(yMin, yMax);
    private readonly double minXSpan = Math.Abs(xMax - xMin) * minSpanFraction;
    private readonly double minYSpan = Math.Abs(yMax - yMin) * minSpanFraction;

    public void Apply(RenderPack rp, bool beforeLayout)
    {
        // Max zoom out: clamp range to the data bounds (inversion-safe).
        xAxis.Min = Math.Clamp(xAxis.Min, xLow, xHigh);
        xAxis.Max = Math.Clamp(xAxis.Max, xLow, xHigh);
        yAxis.Min = Math.Clamp(yAxis.Min, yLow, yHigh);
        yAxis.Max = Math.Clamp(yAxis.Max, yLow, yHigh);

        // Max zoom in: minSpanFraction of the full range. Magnitudes preserve inversion.
        var xSpan = xAxis.Max - xAxis.Min;
        if (Math.Abs(xSpan) < minXSpan)
        {
            var center = (xAxis.Min + xAxis.Max) / 2.0;
            var sign = xSpan < 0 ? -1.0 : 1.0;
            xAxis.Min = center - sign * minXSpan / 2.0;
            xAxis.Max = center + sign * minXSpan / 2.0;
        }

        var ySpan = yAxis.Max - yAxis.Min;
        if (Math.Abs(ySpan) < minYSpan)
        {
            var center = (yAxis.Min + yAxis.Max) / 2.0;
            var sign = ySpan < 0 ? -1.0 : 1.0;
            yAxis.Min = center - sign * minYSpan / 2.0;
            yAxis.Max = center + sign * minYSpan / 2.0;
        }
    }
}

internal class FixedAutoScaler(double? minX = null, double? maxX = null, double? minY = null, double? maxY = null) : IAutoScaler
{
    public AxisLimits GetAxisLimits(Plot plot, IXAxis xAxis, IYAxis yAxis)
    {
        return new AxisLimits(minX ?? xAxis.Min, maxX ?? xAxis.Max, minY ?? yAxis.Min, maxY ?? yAxis.Max);
    }

    public void AutoScaleAll(IEnumerable<IPlottable> plottables)
    {
        var xAxes = plottables.Select(x => x.Axes.XAxis).Distinct();
        var yAxes = plottables.Select(x => x.Axes.YAxis).Distinct();

        foreach (var axis in xAxes)
        {
            var min = minX ?? axis.Min;
            var max = maxX ?? axis.Max;
            axis.Range.Set(min, max);
        }

        foreach (var axis in yAxes)
        {
            var min = minY ?? axis.Min;
            var max = maxY ?? axis.Max;
            axis.Range.Set(min, max);
        }
    }

    public bool InvertedX { get; set; }
    public bool InvertedY { get; set; }
}

public class TelemetryPlot(Plot plot) : SufniPlot(plot)
{
    public static readonly Color FrontColor = Color.FromHex("#3288bd");
    public static readonly Color RearColor = Color.FromHex("#66c2a5");

    public int? MaximumDisplayHz { get; set; }

    protected void ConfigureRightAxisStyle()
    {
        Plot.Axes.Right.TickLabelStyle.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Right.TickLabelStyle.Bold = false;
        Plot.Axes.Right.TickLabelStyle.FontSize = 12;
        Plot.Axes.Right.MajorTickStyle.Length = 0;
        Plot.Axes.Right.MinorTickStyle.Length = 0;
        Plot.Axes.Right.MajorTickStyle.Width = 0;
        Plot.Axes.Right.MinorTickStyle.Width = 0;
    }

    protected void ConfigureTimeTicks()
    {
        Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            TargetTickCount = 20,
            LabelFormatter = value => $"{value:0.###}"
        };
    }

    protected void ConfigureSymmetricValueTicks(float minimumTickSpacing)
    {
        ScottPlot.TickGenerators.NumericAutomatic tickGenerator = new()
        {
            MinimumTickSpacing = minimumTickSpacing
        };

        Plot.Axes.Left.TickGenerator = tickGenerator;
        Plot.Axes.Right.TickGenerator = tickGenerator;
    }

    public virtual void SetCursorPosition(double position) { }

    protected (double[] Samples, double Step) PrepareDisplaySignal(double[] samples, int sampleRate)
    {
        return TelemetryDisplayDownsampling.Prepare(samples, sampleRate, MaximumDisplayHz);
    }

    public virtual void LoadTelemetryData(TelemetryData telemetryData) { }
}