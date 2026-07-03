using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Sessions.Plots;

public class DeepTravelDistributionPlot(Plot plot, SuspensionType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme), ISelectableAnalysisPlot
{
    private readonly SelectableAnalysisBarHitTester hitTester = new();
    private TelemetryRangeSelection? activeAnalysisSelection;

    public bool TryGetRangeSelection(
        double x,
        double y,
        [NotNullWhen(true)] out TelemetryRangeSelection? selection)
    {
        if (hitTester.TryHit(x, y, out var hit) ||
            hitTester.TryHitVerticalBarAtX(x, out hit))
        {
            selection = new DeepTravelRangeSelection(type, hit.Bin);
            return true;
        }

        selection = null;
        return false;
    }

    public void SetActiveAnalysisSelection(TelemetryRangeSelection? selection)
    {
        activeAnalysisSelection = selection;
        ApplyActiveAnalysisSelection();
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange);
        LoadAnalysisData(new DeepTravelDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateDeepTravelHistogram(telemetryData, type, AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData));
    }

    public void LoadAnalysisData(DeepTravelDistributionAnalysisResult data)
    {
        hitTester.Clear();
        if (!data.HasStrokeData || data.Histogram.Bins.Count == 0)
        {
            return;
        }

        ResetTelemetryReadouts();

        SetTitle(AnalysisPlotTitles.DeepTravelDistribution(type));
        SetAxisLabels("Axle position (mm)", "Strokes");
        Plot.Layout.Fixed(CreateAnalysisPlotPadding());

        var histogram = data.Histogram;
        var step = histogram.Bins[1] - histogram.Bins[0];
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = histogram.Values.Index()
            .Where(entry => entry.Item > 0)
            .Select(entry =>
            {
                var bin = TelemetryRangeSelection.BinRange.FromBins(histogram.Bins, entry.Index);
                var bar = new Bar
                {
                    Position = histogram.Bins[entry.Index],
                    Value = entry.Item,
                    FillColor = color.WithOpacity(),
                    LineColor = color,
                    LineWidth = 1.5f,
                    Orientation = Orientation.Vertical,
                    Size = step * 0.65f,
                };
                SelectableAnalysisBarSelection.ApplyPrimaryBarOutline(bar, color, PlotTheme, IsSelectedBin(bin));
                hitTester.Register(bar, bin, entry.Item, entry.Index);

                AddBarReadout(
                    bar,
                    FormatReadoutRange("Axle position", histogram.Bins, entry.Index, "mm"),
                    new CursorReadoutLine("Strokes", entry.Item, string.Empty, color, "0"));

                return bar;
            })
            .ToList();

        if (bars.Count > 0)
        {
            Plot.Add.Bars(bars);
        }

        var maxValue = Math.Max(1, histogram.Values.Max());
        var top = maxValue / 0.9;
        Plot.Axes.SetLimits(left: histogram.Bins[0], right: histogram.Bins[^1], bottom: 0, top: top);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            histogram.Bins[0], histogram.Bins[^1], 0, top, PlotZoomFractions.Analysis));
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(step)
        {
            LabelFormatter = value => $"{value:0.0}"
        };
    }

    private void ApplyActiveAnalysisSelection()
    {
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        SelectableAnalysisBarSelection.ApplyActiveAnalysisSelection(
            hitTester.Hits,
            color,
            PlotTheme,
            IsSelectedBin);
    }

    private bool IsSelectedBin(TelemetryRangeSelection.BinRange bin)
    {
        return activeAnalysisSelection is DeepTravelRangeSelection selection &&
               selection.SuspensionType == type &&
               SelectableAnalysisBarSelection.MatchesSelectedBin(selection.Bin, bin);
    }
}
