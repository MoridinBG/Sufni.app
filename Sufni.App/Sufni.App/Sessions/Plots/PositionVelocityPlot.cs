using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Plots;

public enum PositionVelocitySideMode
{
    Front,
    Rear,
    Both,
}

public enum PositionVelocityDisplayMode
{
    Traces,
    Heatmap,
}

public sealed class PositionVelocityPlot : TelemetryPlot
{
    private const double SingleSideHeatmapOpacity = 1.0;
    private const double BothSideHeatmapOpacity = 0.62;
    private const double TraceAlpha = 0.10;

    // Strokes hold native Skia paths; they are disposed whenever the plot is cleared.
    private readonly List<ViewportReducedScatter> scatters = [];

    public PositionVelocityPlot(Plot plot, SufniTheme? theme = null)
        : base(plot, theme)
    {
        // Every load sets explicit limits. The autoscale pass enumerates the plottable list on
        // the render thread and races with clear-and-reload on rapid re-renders.
        Plot.RenderManager.RenderActions.RemoveAll(
            action => action is ScottPlot.Rendering.RenderActions.AutoScaleUnsetAxes);
    }

    public PositionVelocitySideMode SideMode { get; set; } = PositionVelocitySideMode.Both;

    public PositionVelocityDisplayMode DisplayMode { get; set; } = PositionVelocityDisplayMode.Traces;

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        LoadAnalysisData(new PositionVelocityAnalysisResult(
            PositionVelocityTraceBuilder.Build(telemetryData, AnalysisRange)));
    }

    public void LoadAnalysisData(PositionVelocityAnalysisResult data)
    {
        ResetTelemetryReadouts();
        var traces = data.Traces;
        var limit = traces.VelocityLimitMmPerSecond;
        SetTitle(AnalysisPlotTitles.PositionVelocity());
        SetAxisLabels("Wheel position %", "Wheel velocity (mm/s)");

        var showFront = SideMode is PositionVelocitySideMode.Front or PositionVelocitySideMode.Both;
        var showRear = SideMode is PositionVelocitySideMode.Rear or PositionVelocitySideMode.Both;
        if (DisplayMode == PositionVelocityDisplayMode.Heatmap)
        {
            var opacity = SideMode == PositionVelocitySideMode.Both
                ? BothSideHeatmapOpacity
                : SingleSideHeatmapOpacity;
            if (showFront)
            {
                AddHeatmap(traces.Front, limit, new ScottPlot.Colormaps.Blues(), opacity, "Front %");
            }

            if (showRear)
            {
                AddHeatmap(traces.Rear, limit, new ScottPlot.Colormaps.Magma(), opacity, "Rear %");
            }
        }
        else
        {
            if (showFront)
            {
                AddTraces(traces.Front, FrontColor);
            }

            if (showRear)
            {
                AddTraces(traces.Rear, RearColor);
            }

            if (SideMode == PositionVelocitySideMode.Both)
            {
                // Legend items are explicit so the swatches stay opaque while the traces are faint.
                Plot.Legend.ManualItems.Add(new LegendItem { LabelText = "Front", LineColor = FrontColor, LineWidth = 2 });
                Plot.Legend.ManualItems.Add(new LegendItem { LabelText = "Rear", LineColor = RearColor, LineWidth = 2 });
                ShowSourceLegend();
            }
        }

        var zeroLine = Plot.Add.HorizontalLine(0);
        zeroLine.LineColor = PlotTheme.ReferenceLine.ToScottPlotColor();
        zeroLine.LineWidth = 1;
        zeroLine.EnableAutoscale = false;

        Plot.Axes.SetLimits(0, 100, -limit, limit);
        Plot.Axes.Rules.Add(new BoundedZoomRule(
            Plot.Axes.Bottom,
            Plot.Axes.Left,
            0,
            100,
            -limit,
            limit,
            PlotZoomFractions.Analysis));
    }

    public override void Clear()
    {
        foreach (var scatter in scatters)
        {
            scatter.Dispose();
        }

        scatters.Clear();
        Plot.Legend.ManualItems.Clear();
        base.Clear();
    }

    private void AddTraces(PositionVelocitySideTrace? side, Color color)
    {
        if (side is null)
        {
            return;
        }

        var scatter = new ViewportReducedScatter(side)
        {
            Color = color.WithAlpha(TraceAlpha),
            LineWidth = 1,
        };
        scatter.MarkerStyle.IsVisible = false;

        scatters.Add(scatter);
        Plot.Add.Plottable(scatter);
    }

    private void AddHeatmap(
        PositionVelocitySideTrace? side,
        double velocityLimit,
        IColormap colormap,
        double opacity,
        string label)
    {
        if (side is null)
        {
            return;
        }

        var heatmap = Plot.Add.Heatmap(side.Heatmap);
        heatmap.Position = new CoordinateRect(left: 0, right: 100, bottom: -velocityLimit, top: velocityLimit);
        heatmap.Colormap = colormap;
        heatmap.Opacity = opacity;
        heatmap.FlipRows = true;

        var colorBar = Plot.Add.ColorBar(heatmap);
        colorBar.Label = label;
        colorBar.LabelStyle.ForeColor = PlotTheme.Axis.Label.ToScottPlotColor();
        colorBar.Axis.TickLabelStyle.ForeColor = PlotTheme.Axis.Tick.ToScottPlotColor();
    }
}
