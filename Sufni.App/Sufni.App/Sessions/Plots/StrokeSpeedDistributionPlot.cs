using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Sessions.Plots;

public class StrokeSpeedDistributionPlot(Plot plot, SuspensionType type, BalanceType strokeKind, SufniTheme? theme = null) : TelemetryPlot(plot, theme), ISelectableAnalysisPlot
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
            selection = new StrokeSpeedRangeSelection(type, strokeKind, hit.Bin);
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
        hitTester.Clear();
        if (!TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var strokeName = strokeKind == BalanceType.Compression ? "compression" : "rebound";
        SetTitle(AnalysisPlotTitles.StrokeSpeedDistribution(type, strokeKind));
        SetAxisLabels("Peak stroke speed (mm/s)", "Strokes (%)");
        Plot.Layout.Fixed(CreateAnalysisPlotPadding());

        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(telemetryData, type, strokeKind, AnalysisRange);
        if (data.Values.Sum() <= 0)
        {
            ShowEmptyState(strokeName);
            return;
        }

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
                SelectableAnalysisBarSelection.ApplyPrimaryBarOutline(bar, color, PlotTheme, IsSelectedBin(bin));
                hitTester.Register(bar, bin, entry.Item, entry.Index);

                AddBarReadout(
                    bar,
                    FormatReadoutRange("Peak speed", data.Bins, entry.Index, "mm/s", "0"),
                    new CursorReadoutLine("Strokes", entry.Item, "%", color));

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
            data.Bins[0], data.Bins[^1], 0, top, PlotZoomFractions.Analysis));
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(500);
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
        return activeAnalysisSelection is StrokeSpeedRangeSelection selection &&
               selection.SuspensionType == type &&
               selection.StrokeKind == strokeKind &&
               SelectableAnalysisBarSelection.MatchesSelectedBin(selection.Bin, bin);
    }
}
