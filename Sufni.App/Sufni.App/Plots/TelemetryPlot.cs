using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.SessionGraphs;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

internal static class ZoomFractions
{
    public const double TimeSeries = 0.01;
    public const double Statistics = 0.10;
}

internal static class AxisRangeConstraints
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

internal class LockedVerticalSoftLockedHorizontalRule(IXAxis xAxis, IYAxis yAxis, double xMin, double xMax, double yMin, double yMax, double minSpanFraction = ZoomFractions.TimeSeries) : IAxisRule
{
    private readonly double minXSpan = Math.Abs(xMax - xMin) * minSpanFraction;

    public void Apply(RenderPack rp, bool beforeLayout)
    {
        (xAxis.Min, xAxis.Max) = AxisRangeConstraints.Constrain(
            xAxis.Min,
            xAxis.Max,
            xMin,
            xMax,
            minXSpan);

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

public class TelemetryPlot : SufniPlot
{
    public static readonly Color FrontColor = Color.FromHex("#3288bd");
    public static readonly Color RearColor = Color.FromHex("#66c2a5");
    private static readonly Color markerLineColor = Color.FromHex("#d53e4f").WithAlpha(0.9);
    private static readonly Color cursorTooltipFillColor = Color.FromHex("#15191C").WithAlpha(0.96);
    private static readonly Color cursorTooltipTextColor = Color.FromHex("#F0F0F0");
    private static readonly Color cursorTooltipBorderColor = Color.FromHex("#5A5A5A");
    private const float MarkerLineWidth = 2.0f;
    private readonly List<IPointerReadoutTarget> pointerReadoutTargets = [];
    private Tooltip? cursorTooltip;

    public TelemetryPlot(Plot plot)
        : base(plot)
    {
        HideSourceLegend();
    }

    public int? MaximumDisplayHz { get; set; }
    public PlotSmoothingLevel SmoothingLevel { get; set; }
    public TelemetryTimeRange? AnalysisRange { get; set; }
    public bool HideRightAxis { get; set; }

    protected void ConfigureTimeSeriesFrame(string title, Func<double, string>? timeLabelFormatter = null)
    {
        Plot.Axes.Title.Label.Text = string.Empty;
        SetAxisLabels(string.Empty, string.Empty);
        Plot.Layout.Fixed(SessionGraphSettings.CreateTimeSeriesPlotPadding(!HideRightAxis));
        ConfigureRightAxisStyle();
        Plot.Axes.Top.IsVisible = false;
        ConfigureTimeTicks(labelFormatter: timeLabelFormatter);
    }

    protected void ConfigureRightAxisStyle()
    {
        if (HideRightAxis)
        {
            Plot.Axes.Right.IsVisible = false;
            return;
        }

        Plot.Axes.Right.IsVisible = true;
        Plot.Axes.Right.TickLabelStyle.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Right.TickLabelStyle.Bold = false;
        Plot.Axes.Right.TickLabelStyle.FontSize = 12;
        Plot.Axes.Right.MajorTickStyle.Length = 0;
        Plot.Axes.Right.MinorTickStyle.Length = 0;
        Plot.Axes.Right.MajorTickStyle.Width = 0;
        Plot.Axes.Right.MinorTickStyle.Width = 0;
    }

    protected void ConfigureTimeTicks(int targetTickCount = 20, Func<double, string>? labelFormatter = null)
    {
        Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            TargetTickCount = targetTickCount,
            LabelFormatter = labelFormatter ?? (value => $"{value:0.###}")
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

    protected void ShowSourceLegend()
    {
        Plot.ShowLegend(Alignment.LowerRight);
        Plot.Legend.BackgroundColor = Color.FromHex("#1A1F23");
        Plot.Legend.OutlineColor = Color.FromHex("#343C42");
        Plot.Legend.ShadowColor = Colors.Transparent;
        Plot.Legend.FontColor = Color.FromHex("#D0D0D0");
        Plot.Legend.FontSize = 12;
        Plot.Legend.Padding = new PixelPadding(8, 8, 8, 8);
    }

    protected void HideSourceLegend()
    {
        Plot.Legend.IsVisible = false;
    }

    protected void SetMirroredValueRange(double minimum, double maximum)
    {
        Plot.Axes.Left.Range.Set(minimum, maximum);
        Plot.Axes.Right.Range.Set(minimum, maximum);
    }

    protected void AddMirroredTimeSeriesAxisRules(double xMinimum, double xMaximum, double yMinimum, double yMaximum)
    {
        SetMirroredValueRange(yMinimum, yMaximum);
        Plot.Axes.Rules.Add(new LockedVerticalSoftLockedHorizontalRule(
            Plot.Axes.Bottom,
            Plot.Axes.Left,
            xMinimum,
            xMaximum,
            yMinimum,
            yMaximum));
        Plot.Axes.Rules.Add(new LockedVerticalSoftLockedHorizontalRule(
            Plot.Axes.Bottom,
            Plot.Axes.Right,
            xMinimum,
            xMaximum,
            yMinimum,
            yMaximum));
    }

    protected VerticalLine AddTimeSeriesCursorLine(bool isVisible = true)
    {
        var line = Plot.Add.VerticalLine(double.NaN);
        line.LineWidth = 1;
        line.LineColor = Colors.LightGray;
        line.IsVisible = isVisible;
        return line;
    }

    protected void ShowTimeSeriesEmptyState(string message, double durationSeconds)
    {
        var xMaximum = double.IsFinite(durationSeconds) && durationSeconds > 0
            ? durationSeconds
            : 1.0;

        Plot.Axes.SetLimits(0, xMaximum, 0, 1);
        AddMirroredTimeSeriesAxisRules(0, xMaximum, 0, 1);

        var text = Plot.Add.Text(message, xMaximum / 2.0, 0.5);
        text.LabelFontColor = Color.FromHex("#fefefe");
        text.LabelFontSize = 13;
        text.LabelAlignment = Alignment.MiddleCenter;
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
        ShowCursorReadout(GetCursorReadout(position));
    }

    public void SetPointerPositionWithReadout(double x, double y)
    {
        ShowCursorReadout(GetPointerReadout(new Coordinates(x, y)));
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

    protected virtual CursorReadout? GetPointerReadout(Coordinates position)
    {
        if (pointerReadoutTargets.Count == 0)
        {
            return null;
        }

        IPointerReadoutTarget? nearest = null;
        var nearestDistance = double.PositiveInfinity;
        foreach (var readout in pointerReadoutTargets)
        {
            var distance = readout.GetDistanceSquared(position, Plot);
            if (distance < nearestDistance)
            {
                nearest = readout;
                nearestDistance = distance;
            }
        }

        return nearest?.ToCursorReadout(position, Plot);
    }

    protected void AddBarReadout(
        Bar bar,
        string header,
        params CursorReadoutLine[] lines)
    {
        pointerReadoutTargets.Add(StatisticsBarReadout.FromBar(bar, header, lines));
    }

    private protected void AddPointerReadoutTarget(IPointerReadoutTarget readoutTarget)
    {
        pointerReadoutTargets.Add(readoutTarget);
    }

    protected static string FormatReadoutRange(
        string label,
        IReadOnlyList<double> bins,
        int index,
        string unit,
        string format = "0.#")
    {
        if (index + 1 >= bins.Count)
        {
            return $"{label}: {FormatReadoutValue(bins[index], unit, format)}";
        }

        return $"{label}: {FormatReadoutValue(bins[index], unit, format)}-" +
               $"{FormatReadoutValue(bins[index + 1], unit, format)}";
    }

    protected static string FormatReadoutValue(
        double value,
        string unit,
        string format = "0.#")
    {
        var formatted = value.ToString(format, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit)
            ? formatted
            : $"{formatted} {unit}";
    }

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
        return (TelemetryDisplaySmoothing.ApplyRegular(displaySamples, SmoothingLevel, step), step);
    }

    public override void Clear()
    {
        base.Clear();
        HideSourceLegend();
        ResetReadouts();
    }

    public virtual void LoadTelemetryData(TelemetryData telemetryData)
    {
        ResetReadouts();
    }

    private void ResetReadouts()
    {
        ResetCursorReadout();
        pointerReadoutTargets.Clear();
    }

    private void ShowCursorReadout(CursorReadout? readout)
    {
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

        if (readout.KeepTooltipInsideDataArea)
        {
            return GetClampedPointerTooltipLabel(readout, cursorPixelX, Plot.GetPixel(anchor).Y, dataRect);
        }

        // Distance between the cursor line and the nearest edge of the balloon.
        const float pixelOffset = 12f;

        var placeRight = cursorPixelX <= dataRect.Center.X;
        var labelPixelX = placeRight ? cursorPixelX + pixelOffset : cursorPixelX - pixelOffset;
        var alignment = placeRight ? Alignment.MiddleLeft : Alignment.MiddleRight;

        var labelCoord = Plot.GetCoordinates(labelPixelX, centerPixelY);
        return (labelCoord, alignment);
    }

    private (Coordinates Location, Alignment Alignment) GetClampedPointerTooltipLabel(
        CursorReadout readout,
        float cursorPixelX,
        float cursorPixelY,
        PixelRect dataRect)
    {
        const float graphInset = 8f;
        const float pixelOffset = 12f;

        var (estimatedWidth, estimatedHeight) = EstimateTooltipSize(FormatCursorReadout(readout));
        var placeRight = cursorPixelX <= dataRect.Center.X;
        var alignment = placeRight ? Alignment.MiddleLeft : Alignment.MiddleRight;
        var labelPixelX = placeRight
            ? cursorPixelX + pixelOffset
            : cursorPixelX - pixelOffset;

        var dataWidth = Math.Abs(dataRect.Right - dataRect.Left);
        if (estimatedWidth >= dataWidth - graphInset * 2)
        {
            alignment = Alignment.MiddleCenter;
            labelPixelX = dataRect.Center.X;
        }
        else if (placeRight)
        {
            labelPixelX = Math.Clamp(
                labelPixelX,
                dataRect.Left + graphInset,
                dataRect.Right - estimatedWidth - graphInset);
        }
        else
        {
            labelPixelX = Math.Clamp(
                labelPixelX,
                dataRect.Left + estimatedWidth + graphInset,
                dataRect.Right - graphInset);
        }

        var halfHeight = estimatedHeight / 2.0f;
        var minY = dataRect.Top + halfHeight + graphInset;
        var maxY = dataRect.Bottom - halfHeight - graphInset;
        var labelPixelY = minY <= maxY
            ? Math.Clamp(cursorPixelY, minY, maxY)
            : dataRect.Center.Y;

        var labelCoord = Plot.GetCoordinates(labelPixelX, labelPixelY);
        return (labelCoord, alignment);
    }

    private static (float Width, float Height) EstimateTooltipSize(string text)
    {
        const float fontSize = 12f;
        const float horizontalPadding = 20f;
        const float verticalPadding = 16f;
        const float averageCharacterWidth = fontSize * 0.62f;
        const float lineHeight = fontSize * 1.25f;

        var lines = text.Split(Environment.NewLine);
        var maxLineLength = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
        return (
            Width: maxLineLength * averageCharacterWidth + horizontalPadding,
            Height: lines.Length * lineHeight + verticalPadding);
    }

    private static string FormatCursorReadout(CursorReadout readout)
    {
        var lines = new List<string>(readout.Lines.Count + 1);

        if (!string.IsNullOrWhiteSpace(readout.Header))
        {
            lines.Add(readout.Header);
        }
        else if (double.IsFinite(readout.TimeSeconds))
        {
            lines.Add($"{readout.TimeSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
        }

        lines.AddRange(readout.Lines.Select(line => $"{line.Label}: {line.FormatValue()}"));
        return string.Join(Environment.NewLine, lines);
    }
}
