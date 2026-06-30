using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Shared.Formatting;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Sessions.Plots;

public class VelocityHistogramPlot(Plot plot, SuspensionType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme), ISelectableStatisticsPlot
{
    private const double VelocityLimit = SessionDampingSettings.VelocityHistogramLimitMmPerSecond;
    private static readonly IReadOnlyList<Color> palette =
        TravelZonePalette.HexColors.Select(Color.FromHex).ToArray();

    private readonly List<VelocityHistogramSelectableSegment> selectableSegments = [];
    private IReadOnlyList<double> selectableVelocityBins = [];
    private TelemetryRangeSelection? selectedRangeSelection;

    public VelocityAverageMode AverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
    public DampingSpeedCutoffs DampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;

    public bool TryGetRangeSelection(
        double x,
        double y,
        [NotNullWhen(true)] out TelemetryRangeSelection? selection)
    {
        selection = null;
        if (selectableVelocityBins.Count < 2)
        {
            return false;
        }

        foreach (var segment in selectableSegments)
        {
            if (x >= segment.XMinimum &&
                x <= segment.XMaximum &&
                y >= segment.YMinimum &&
                y <= segment.YMaximum)
            {
                selection = new DampingRangeSelection(
                    type,
                    AverageMode,
                    segment.VelocityBinIndex,
                    segment.TravelBinIndex,
                    segment.TravelBinIndex);
                return true;
            }
        }

        if (!TryGetVelocityBinIndex(y, out var velocityBinIndex))
        {
            return false;
        }

        selection = new DampingRangeSelection(
            type,
            AverageMode,
            velocityBinIndex,
            0,
            TelemetryData.TravelBinsForVelocityHistogram - 1);
        return true;
    }

    public void SetSelectedRangeSelection(TelemetryRangeSelection? selection)
    {
        selectedRangeSelection = selection;
        ApplySelectedRangeSelection();
    }

    private void AddStatistics(VelocityStatistics statistics)
    {
        var maxReboundVelString = FormatVelocityLabel(statistics.MaxRebound);
        var percentileReboundString = $"95%: {FormatVelocityLabel(statistics.Percentile95Rebound)}";
        var avgReboundVelString = FormatVelocityLabel(statistics.AverageRebound);
        var avgCompVelString = FormatVelocityLabel(statistics.AverageCompression);
        var percentileCompString = $"95%: {FormatVelocityLabel(statistics.Percentile95Compression)}";
        var maxCompVelString = FormatVelocityLabel(statistics.MaxCompression);

        // TODO: Restore original behaviour: label at bottom when not in range, but moves to its proper
        // place when it is scrolled into view.
        AddLabelWithHorizontalLine(maxReboundVelString, statistics.MaxRebound, LabelLinePosition.Above);
        if (statistics.ReboundStrokeCount > 0)
        {
            AddLabelWithHorizontalLine(percentileReboundString, statistics.Percentile95Rebound, LabelLinePosition.Above);
        }

        // Average values should be between the hardcoded limits, it's safe to draw them
        // at their actual position.
        AddLabelWithHorizontalLine(avgReboundVelString, statistics.AverageRebound, LabelLinePosition.Below);
        AddLabelWithHorizontalLine(avgCompVelString, statistics.AverageCompression, LabelLinePosition.Above);
        if (statistics.CompressionStrokeCount > 0)
        {
            AddLabelWithHorizontalLine(percentileCompString, statistics.Percentile95Compression, LabelLinePosition.Below);
        }

        // TODO: Restore original behaviour: label at bottom when not in range, but moves to its proper
        // place when it is scrolled into view.
        AddLabelWithHorizontalLine(maxCompVelString, statistics.MaxCompression, LabelLinePosition.Below);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        selectableSegments.Clear();
        selectableVelocityBins = [];

        if (!TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var isStrokePeakMode = AverageMode == VelocityAverageMode.StrokePeakAveraged;
        var percentageLabel = isStrokePeakMode ? "Strokes" : "Time";
        SetTitle(StatisticsPlotTitles.VelocityHistogram(type, AverageMode));
        SetAxisLabels(isStrokePeakMode ? "Strokes (%)" : "Time (%)", "Velocity (mm/s)");
        Plot.Layout.Fixed(CreateStatisticsPlotPadding(right: 5));

        var data = TelemetryStatistics.CalculateVelocityHistogram(telemetryData, type, CreateOptions());
        var step = data.Bins[1] - data.Bins[0];
        selectableVelocityBins = data.Bins;

        for (var i = 0; i < data.Values.Count; ++i)
        {
            double nextBarBase = 0;

            for (var j = 0; j < TelemetryData.TravelBinsForVelocityHistogram; j++)
            {
                if (data.Values[i][j] == 0)
                {
                    continue;
                }

                var value = data.Values[i][j];
                var bar = new Bar
                {
                    Position = data.Bins[i],
                    ValueBase = nextBarBase,
                    Value = nextBarBase + value,
                    FillColor = palette[j].WithOpacity(0.8),
                    LineColor = Colors.Black,
                    LineWidth = 0.5f,
                    Orientation = Orientation.Horizontal,
                    Size = step * 0.95
                };
                ApplyPrimarySegmentOutline(bar, isSelected: IsSelectedSegment(i, j));

                Plot.Add.Bar(bar);
                selectableSegments.Add(new VelocityHistogramSelectableSegment(
                    bar,
                    i,
                    j,
                    Math.Min(bar.ValueBase, bar.Value),
                    Math.Max(bar.ValueBase, bar.Value),
                    bar.Position - bar.Size / 2.0,
                    bar.Position + bar.Size / 2.0));
                AddBarReadout(
                    bar,
                    $"{FormatReadoutRange("Velocity", data.Bins, i, "mm/s", "0")}{Environment.NewLine}Travel: {j * 10}-{(j + 1) * 10} %",
                    new CursorReadoutLine(percentageLabel, value, "%", palette[j]));

                nextBarBase += value;
            }
        }

        Plot.Axes.AutoScale(invertY: true);
        var limits = Plot.Axes.GetDataLimits();
        Plot.Axes.AutoScaler = new FixedAutoScaler(minX: 0.1, minY: 2000, maxY: -2000);

        // Y bounds must include the max-compression/-rebound stats labels, which can sit
        // outside the hardcoded ±VelocityLimit display window.
        var velocityStats = TelemetryStatistics.CalculateVelocityStatistics(telemetryData, type, CreateOptions());
        var yLow = Math.Min(-VelocityLimit, velocityStats.MaxRebound);
        if (velocityStats.ReboundStrokeCount > 0)
        {
            yLow = Math.Min(yLow, velocityStats.Percentile95Rebound);
        }

        var yHigh = Math.Max(VelocityLimit, velocityStats.MaxCompression);
        if (velocityStats.CompressionStrokeCount > 0)
        {
            yHigh = Math.Max(yHigh, velocityStats.Percentile95Compression);
        }

        // Lock axes
        Plot.Axes.Rules.Add(new LockedHorizontal(Plot.Axes.Bottom, 0.1, limits.Right / 0.9));
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0.1, limits.Right / 0.9, yLow, yHigh, ZoomFractions.Statistics));

        // Set left axis limit to 0.1 to hide the border line at 0 values. Otherwise
        // it would seem that there are actual measure travel data there too.
        // Also set a hardcoded limit for the velocity range.
        Plot.Axes.SetLimits(left: 0.1,
            right: limits.Right / 0.9,
            bottom: VelocityLimit,
            top: -VelocityLimit);

        Plot.Axes.Left.TickGenerator = new NumericFixedInterval(500);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(2);

        var normalData = AverageMode == VelocityAverageMode.SampleAveraged
            ? TelemetryStatistics.CalculateNormalDistribution(telemetryData, type, AnalysisRange)
            : new NormalDistributionData([], []);
        if (normalData.Pdf.Count > 0 && normalData.Y.Count > 0)
        {
            var normal = Plot.Add.Scatter(
                normalData.Pdf.ToArray(),
                normalData.Y.ToArray());
            normal.Color = Color.FromHex("#d53e4f");
            normal.MarkerStyle.IsVisible = false;
            normal.LineStyle.Width = 2;
            normal.LineStyle.Pattern = LinePattern.DenselyDashed;
        }

        AddStatistics(velocityStats);
    }

    private bool TryGetVelocityBinIndex(double y, out int velocityBinIndex)
    {
        velocityBinIndex = default;
        if (selectableVelocityBins.Count < 2 ||
            y < selectableVelocityBins[0] ||
            y > selectableVelocityBins[^1])
        {
            return false;
        }

        velocityBinIndex = HistogramBuilder.DigitizeValue(y, selectableVelocityBins.ToArray());
        return true;
    }

    private void ApplySelectedRangeSelection()
    {
        foreach (var segment in selectableSegments)
        {
            ApplyPrimarySegmentOutline(
                segment.Bar,
                IsSelectedSegment(segment.VelocityBinIndex, segment.TravelBinIndex));
        }
    }

    private bool IsSelectedSegment(int velocityBinIndex, int travelBinIndex)
    {
        return selectedRangeSelection is DampingRangeSelection selection &&
               selection.SuspensionType == type &&
               selection.AverageMode == AverageMode &&
               selection.VelocityBinIndex == velocityBinIndex &&
               travelBinIndex >= selection.TravelBinStartIndex &&
               travelBinIndex <= selection.TravelBinEndIndex;
    }

    private void ApplyPrimarySegmentOutline(Bar bar, bool isSelected)
    {
        if (isSelected)
        {
            bar.LineColor = PlotTheme.Marker.DampingSelectionOutline.ToScottPlotColor();
            bar.LineWidth = 3.0f;
            return;
        }

        bar.LineColor = Colors.Black;
        bar.LineWidth = 0.5f;
    }

    private VelocityStatisticsOptions CreateOptions()
    {
        var cutoffs = DampingSpeedCutoffs.ForSide(type);
        return new VelocityStatisticsOptions(
            AnalysisRange,
            AverageMode,
            cutoffs.CompressionMmPerSecond,
            cutoffs.ReboundMmPerSecond);
    }

    private static string FormatVelocityLabel(double value) =>
        $"{UnitsFormatter.FormatNumber(value, 1)} mm/s";

    private readonly record struct VelocityHistogramSelectableSegment(
        Bar Bar,
        int VelocityBinIndex,
        int TravelBinIndex,
        double XMinimum,
        double XMaximum,
        double YMinimum,
        double YMaximum);
}
