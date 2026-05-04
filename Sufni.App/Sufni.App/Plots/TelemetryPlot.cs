using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
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
    private static readonly Color markerLineColor = Color.FromHex("#d53e4f").WithAlpha(0.9);
    private static readonly Color cursorTooltipFillColor = Color.FromHex("#15191C").WithAlpha(0.96);
    private static readonly Color cursorTooltipTextColor = Color.FromHex("#F0F0F0");
    private static readonly Color cursorTooltipBorderColor = Color.FromHex("#5A5A5A");
    private const float MarkerLineWidth = 2.0f;
    private Tooltip? cursorTooltip;

    public int? MaximumDisplayHz { get; set; }
    public PlotSmoothingLevel SmoothingLevel { get; set; }
    public TelemetryTimeRange? AnalysisRange { get; set; }

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

    protected void AddMarkerLines(TelemetryData telemetryData)
    {
        foreach (var marker in telemetryData.Markers)
        {
            if (double.IsNaN(marker.TimestampOffset) || double.IsInfinity(marker.TimestampOffset))
            {
                continue;
            }

            var markerSeconds = telemetryData.Metadata.Duration > 0
                ? Math.Clamp(marker.TimestampOffset, 0, telemetryData.Metadata.Duration)
                : marker.TimestampOffset;
            var line = Plot.Add.VerticalLine(markerSeconds);
            line.LineWidth = MarkerLineWidth;
            line.LineColor = markerLineColor;
        }
    }

    public virtual void SetCursorPosition(double position)
    {
        SetCursorLinePosition(position);
        HideCursorReadout();
    }

    public void SetCursorPositionWithReadout(double position)
    {
        SetCursorLinePosition(position);
        ShowCursorReadout(position);
    }

    public void HideCursorReadout()
    {
        if (cursorTooltip is not null)
        {
            cursorTooltip.IsVisible = false;
        }
    }

    protected virtual void SetCursorLinePosition(double position) { }

    protected virtual CursorReadout? GetCursorReadout(double position) => null;

    protected static CursorReadout? CreateCursorReadout(
        double position,
        double maximumPosition,
        IReadOnlyList<CursorReadoutSeries> series)
    {
        if (!double.IsFinite(position) ||
            !double.IsFinite(maximumPosition) ||
            maximumPosition <= 0 ||
            position < 0 ||
            position > maximumPosition)
        {
            return null;
        }

        var lines = new List<CursorReadoutLine>();
        foreach (var item in series)
        {
            if (item.TryGetLine(position, out var line))
            {
                lines.Add(line);
            }
        }

        return lines.Count == 0
            ? null
            : new CursorReadout(position, position, lines[0].Value, lines);
    }

    protected void ResetCursorReadout()
    {
        cursorTooltip = null;
    }

    protected (double[] Samples, double Step) PrepareDisplaySignal(double[] samples, int sampleRate)
    {
        var (displaySamples, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate, MaximumDisplayHz);
        return (TelemetryDisplaySmoothing.Apply(displaySamples, SmoothingLevel), step);
    }

    public virtual void LoadTelemetryData(TelemetryData telemetryData)
    {
        ResetCursorReadout();
    }

    private void ShowCursorReadout(double position)
    {
        if (!double.IsFinite(position))
        {
            HideCursorReadout();
            return;
        }

        var readout = GetCursorReadout(position);
        if (readout is null || readout.Lines.Count == 0)
        {
            HideCursorReadout();
            return;
        }

        var tooltip = cursorTooltip ??= CreateCursorTooltip(readout);
        var (labelLocation, alignment) = GetCursorTooltipLabel(readout);

        tooltip.LabelText = FormatCursorReadout(readout);
        // TipLocation is set to the label location so the balloon's tail collapses —
        // we want the balloon shape without an anchor pointing at a data point.
        tooltip.TipLocation = labelLocation;
        tooltip.LabelLocation = labelLocation;
        tooltip.LabelAlignment = alignment;
        tooltip.IsVisible = true;
    }

    private Tooltip CreateCursorTooltip(CursorReadout readout)
    {
        var (labelLocation, alignment) = GetCursorTooltipLabel(readout);
        var tooltip = Plot.Add.Tooltip(
            labelLocation.X,
            labelLocation.Y,
            FormatCursorReadout(readout),
            labelLocation.X,
            labelLocation.Y);

        tooltip.IsVisible = false;
        tooltip.FillColor = cursorTooltipFillColor;
        tooltip.LabelBackgroundColor = cursorTooltipFillColor;
        tooltip.LabelFontColor = cursorTooltipTextColor;
        tooltip.LabelFontSize = 12;
        tooltip.LabelBorderWidth = 0;
        tooltip.LineWidth = 1;
        tooltip.LineColor = cursorTooltipBorderColor;
        tooltip.LabelAlignment = alignment;
        tooltip.Padding = 6;
        return tooltip;
    }

    private (Coordinates Location, Alignment Alignment) GetCursorTooltipLabel(CursorReadout readout)
    {
        var anchor = new Coordinates(readout.AnchorX, readout.AnchorY);
        var dataRect = Plot.LastRender.DataRect;

        // Before the first render the data rect is empty; we don't have a pixel-space
        // mapping yet, so fall back to placing at the anchor. The next render will
        // recompute with a real DataRect.
        if (dataRect.Width <= 0 || dataRect.Height <= 0)
        {
            return (anchor, Alignment.MiddleCenter);
        }

        var cursorPixelX = Plot.GetPixel(anchor).X;
        var centerPixelY = dataRect.Center.Y;

        // Distance between the cursor line and the nearest edge of the balloon.
        const float pixelOffset = 12f;

        var placeRight = cursorPixelX <= dataRect.Center.X;
        var labelPixelX = placeRight ? cursorPixelX + pixelOffset : cursorPixelX - pixelOffset;
        var alignment = placeRight ? Alignment.MiddleLeft : Alignment.MiddleRight;

        var labelCoord = Plot.GetCoordinates(labelPixelX, centerPixelY);
        return (labelCoord, alignment);
    }

    private static string FormatCursorReadout(CursorReadout readout)
    {
        var lines = new List<string>(readout.Lines.Count + 1)
        {
            $"{readout.TimeSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s"
        };

        lines.AddRange(readout.Lines.Select(line => $"{line.Label}: {line.FormatValue()}"));
        return string.Join(Environment.NewLine, lines);
    }
}
