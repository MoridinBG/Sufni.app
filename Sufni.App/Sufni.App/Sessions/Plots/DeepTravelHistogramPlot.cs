using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Sessions.Plots;

public class DeepTravelHistogramPlot(Plot plot, SuspensionType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme), ISelectableStatisticsPlot
{
    private readonly SelectableStatisticsBarHitTester hitTester = new();
    private TelemetryRangeSelection? selectedRangeSelection;

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

    public void SetSelectedRangeSelection(TelemetryRangeSelection? selection)
    {
        selectedRangeSelection = selection;
        ApplySelectedRangeSelection();
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        hitTester.Clear();
        if (!TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        SetTitle(StatisticsPlotTitles.DeepTravelHistogram(type));
        SetAxisLabels("Axle position (mm)", "Strokes");
        Plot.Layout.Fixed(CreateStatisticsPlotPadding());

        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetryData, type, AnalysisRange);
        var step = data.Bins[1] - data.Bins[0];
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = data.Values.Index()
            .Where(entry => entry.Item > 0)
            .Select(entry =>
            {
                var bin = TelemetryRangeSelection.BinRange.FromBins(data.Bins, entry.Index);
                var bar = new Bar
                {
                    Position = data.Bins[entry.Index],
                    Value = entry.Item,
                    FillColor = color.WithOpacity(),
                    LineColor = color,
                    LineWidth = 1.5f,
                    Orientation = Orientation.Vertical,
                    Size = step * 0.65f,
                };
                SelectableStatisticsBarSelection.ApplyPrimaryBarOutline(bar, color, PlotTheme, IsSelectedBin(bin));
                hitTester.Register(bar, bin, entry.Item, entry.Index);

                AddBarReadout(
                    bar,
                    FormatReadoutRange("Axle position", data.Bins, entry.Index, "mm"),
                    new CursorReadoutLine("Strokes", entry.Item, string.Empty, color, "0"));

                return bar;
            })
            .ToList();

        if (bars.Count > 0)
        {
            Plot.Add.Bars(bars);
        }

        var maxValue = Math.Max(1, data.Values.Max());
        var top = maxValue / 0.9;
        Plot.Axes.SetLimits(left: data.Bins[0], right: data.Bins[^1], bottom: 0, top: top);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            data.Bins[0], data.Bins[^1], 0, top, ZoomFractions.Statistics));
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(step)
        {
            LabelFormatter = value => $"{value:0.0}"
        };
    }

    private void ApplySelectedRangeSelection()
    {
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        SelectableStatisticsBarSelection.ApplySelectedRangeSelection(
            hitTester.Hits,
            color,
            PlotTheme,
            IsSelectedBin);
    }

    private bool IsSelectedBin(TelemetryRangeSelection.BinRange bin)
    {
        return selectedRangeSelection is DeepTravelRangeSelection selection &&
               selection.SuspensionType == type &&
               SelectableStatisticsBarSelection.MatchesSelectedBin(selection.Bin, bin);
    }
}
