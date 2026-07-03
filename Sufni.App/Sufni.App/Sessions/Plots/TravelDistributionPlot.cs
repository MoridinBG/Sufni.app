using System.Linq;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Sessions.Plots;

public class TravelDistributionPlot(Plot plot, SuspensionType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    public TravelDistributionMode HistogramMode { get; set; } = TravelDistributionMode.ActiveSuspension;

    private void AddStatistics(TravelDistributionAnalysisResult data)
    {
        var statistics = data.Statistics;

        var avgPercentage = data.MaxTravel is > 0
            ? statistics.Average / data.MaxTravel.Value * 100.0
            : 0;
        var maxPercentage = data.MaxTravel is > 0
            ? statistics.Max / data.MaxTravel.Value * 100.0
            : 0;

        var avgString = $"{statistics.Average:F1} mm ({avgPercentage:F1}%)";
        var bottomoutLabel = HistogramMode == TravelDistributionMode.DynamicSag
            ? FormatCount(statistics.Bottomouts, "bottom-out region", "bottom-out regions")
            : FormatCount(statistics.Bottomouts, "stroke bottom-out", "stroke bottom-outs");
        var maxString = $"{statistics.Max:F1} mm ({maxPercentage:F1}%) / {bottomoutLabel}";

        AddLabelWithHorizontalLine(avgString, statistics.Average, LabelLinePosition.Above);
        AddLabelWithHorizontalLine(maxString, statistics.Max, LabelLinePosition.Below);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var suspension = type == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange);
        if (HistogramMode == TravelDistributionMode.ActiveSuspension && !hasStrokeData)
        {
            LoadAnalysisData(new TravelDistributionAnalysisResult(
                new HistogramData([], []),
                new TravelStatistics(0, 0, 0),
                suspension.MaxTravel,
                HasStrokeData: false));
            return;
        }

        var options = CreateOptions();
        LoadAnalysisData(new TravelDistributionAnalysisResult(
            TelemetryStatistics.CalculateTravelHistogram(telemetryData, type, options),
            TelemetryStatistics.CalculateTravelStatistics(telemetryData, type, options),
            suspension.MaxTravel,
            hasStrokeData));
    }

    public void LoadAnalysisData(TravelDistributionAnalysisResult data)
    {
        if (HistogramMode == TravelDistributionMode.ActiveSuspension && !data.HasStrokeData)
        {
            return;
        }

        if (data.Histogram.Values.Sum() <= 0)
        {
            return;
        }

        ResetTelemetryReadouts();

        SetTitle(AnalysisPlotTitles.TravelDistribution(type, HistogramMode));
        SetAxisLabels("Time (%)", "Axle position (mm)");
        Plot.Layout.Fixed(CreateAnalysisPlotPadding());

        var histogram = data.Histogram;
        var step = histogram.Bins[1] - histogram.Bins[0];
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = histogram.Values.Index().Select(item =>
            {
                var bar = new Bar
                {
                    Position = histogram.Bins[item.Index],
                    Value = item.Item,
                    FillColor = color.WithOpacity(),
                    LineColor = color,
                    LineWidth = 1.5f,
                    Orientation = Orientation.Horizontal,
                    Size = step * 0.65f,
                };

                AddBarReadout(
                    bar,
                    FormatReadoutRange("Axle position", histogram.Bins, item.Index, "mm"),
                    new CursorReadoutLine("Time", item.Item, "%", color));

                return bar;
            })
            .ToList();

        Plot.Add.Bars(bars);
        Plot.Axes.AutoScale(invertY: true);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(2);

        // Lock horizontal axis, bound vertical zoom (X is already locked, so X args here are inert).
        Plot.Axes.Rules.Add(new LockedHorizontal(Plot.Axes.Bottom, 0.05, histogram.Values.Max() / 0.9));
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0.05, histogram.Values.Max() / 0.9, histogram.Bins[0], histogram.Bins[^1], PlotZoomFractions.Analysis));

        // Set to 0.05 to hide the border line at 0 values. Otherwise it would
        // seem that there are actual measure travel data there too.
        Plot.Axes.SetLimits(left: 0.05);

        AddStatistics(data);
    }

    private TravelStatisticsOptions CreateOptions() => new(AnalysisRange, HistogramMode);

    private static string FormatCount(int count, string singular, string plural) =>
        count == 1 ? $"{count} {singular}" : $"{count} {plural}";
}
