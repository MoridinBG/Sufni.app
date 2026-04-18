using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

internal class LockedVerticalSoftLockedHorizontalRule(IXAxis xAxis, IYAxis yAxis, double xMin, double xMax, double yMin, double yMax) : IAxisRule
{
    public void Apply(RenderPack rp, bool beforeLayout)
    {
        if (xAxis.Min < xMin) xAxis.Min = xMin;
        if (xAxis.Max > xMax) xAxis.Max = xMax;
        yAxis.Range.Set(yMin, yMax);
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

    public virtual void LoadTelemetryData(TelemetryData telemetryData) { }
}