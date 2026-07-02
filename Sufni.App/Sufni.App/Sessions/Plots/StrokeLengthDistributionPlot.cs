using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Sessions.Plots;

public class StrokeLengthDistributionPlot(Plot plot, SuspensionType type, BalanceType strokeKind, SufniTheme? theme = null) : TelemetryPlot(plot, theme), ISelectableAnalysisPlot
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
            selection = new StrokeLengthRangeSelection(type, strokeKind, hit.Bin);
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
        LoadAnalysisData(new StrokeLengthDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateStrokeLengthHistogram(telemetryData, type, strokeKind, AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData));
    }

    public void LoadAnalysisData(StrokeLengthDistributionAnalysisResult data)
    {
        hitTester.Clear();
        if (!data.HasStrokeData || data.Histogram.Bins.Count == 0)
        {
            return;
        }

        ResetTelemetryReadouts();

        var strokeName = strokeKind == BalanceType.Compression ? "compression" : "rebound";
        SetTitle(AnalysisPlotTitles.StrokeLengthDistribution(type, strokeKind));
        SetAxisLabels("Stroke length (mm)", "Strokes (%)");
        Plot.Layout.Fixed(CreateAnalysisPlotPadding());

        var histogram = data.Histogram;
        if (histogram.Values.Sum() <= 0)
        {
            ShowEmptyState(strokeName);
            return;
        }

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
                    FormatReadoutRange("Stroke length", histogram.Bins, entry.Index, "mm"),
                    new CursorReadoutLine("Strokes", entry.Item, "%", color));

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
            histogram.Bins[0], histogram.Bins[^1], 0, top, ZoomFractions.Analysis));
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(step * 2)
        {
            LabelFormatter = value => $"{value:0.0}"
        };
    }

    private void ShowEmptyState(string strokeName)
    {
        Plot.Axes.SetLimits(0, 1, 0, 1);
        AddLabel($"No {strokeName} strokes", 0.5, 0.5, 0, 0, Alignment.MiddleCenter);
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
        return activeAnalysisSelection is StrokeLengthRangeSelection selection &&
               selection.SuspensionType == type &&
               selection.StrokeKind == strokeKind &&
               SelectableAnalysisBarSelection.MatchesSelectedBin(selection.Bin, bin);
    }
}
