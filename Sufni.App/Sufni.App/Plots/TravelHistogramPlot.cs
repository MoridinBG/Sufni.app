using System.Linq;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelHistogramPlot(Plot plot, SuspensionType type) : TelemetryPlot(plot)
{
    public TravelHistogramMode HistogramMode { get; set; } = TravelHistogramMode.ActiveSuspension;

    private void AddStatistics(TelemetryData telemetryData)
    {
        var statistics = TelemetryStatistics.CalculateTravelStatistics(telemetryData, type, CreateOptions());

        var mx = type == SuspensionType.Front
            ? telemetryData.Front.MaxTravel
            : telemetryData.Rear.MaxTravel;
        var avgPercentage = statistics.Average / mx * 100.0;
        var maxPercentage = statistics.Max / mx * 100.0;

        var avgString = $"{statistics.Average:F1} mm ({avgPercentage:F1}%)";
        var bottomoutLabel = HistogramMode == TravelHistogramMode.DynamicSag
            ? FormatCount(statistics.Bottomouts, "bottom-out region", "bottom-out regions")
            : FormatCount(statistics.Bottomouts, "stroke bottom-out", "stroke bottom-outs");
        var maxString = $"{statistics.Max:F1} mm ({maxPercentage:F1}%) / {bottomoutLabel}";

        AddLabelWithHorizontalLine(avgString, statistics.Average, LabelLinePosition.Above);
        AddLabelWithHorizontalLine(maxString, statistics.Max, LabelLinePosition.Below);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (HistogramMode == TravelHistogramMode.ActiveSuspension && !TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange))
        {
            return;
        }

        var data = TelemetryStatistics.CalculateTravelHistogram(telemetryData, type, CreateOptions());
        if (data.Values.Sum() <= 0)
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var modeLabel = HistogramMode == TravelHistogramMode.DynamicSag ? "dynamic sag" : "active suspension";
        Plot.Axes.Title.Label.Text = type == SuspensionType.Front
            ? $"Front travel - {modeLabel} (time% / mm)"
            : $"Rear travel - {modeLabel} (time% / mm)";
        Plot.Layout.Fixed(new PixelPadding(40, 10, 40, 40));

        var step = data.Bins[1] - data.Bins[0];
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = data.Bins.Zip(data.Values)
            .Select(tuple => new Bar
            {
                Position = tuple.First,
                Value = tuple.Second,
                FillColor = color.WithOpacity(),
                LineColor = color,
                LineWidth = 1.5f,
                Orientation = Orientation.Horizontal,
                Size = step * 0.65f,
            })
            .ToList();

        Plot.Add.Bars(bars);
        Plot.Axes.AutoScale(invertY: true);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(2);

        // Lock horizontal axis, bound vertical zoom (X is already locked, so X args here are inert).
        Plot.Axes.Rules.Add(new LockedHorizontal(Plot.Axes.Bottom, 0.05, data.Values.Max() / 0.9));
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0.05, data.Values.Max() / 0.9, data.Bins[0], data.Bins[^1], ZoomFractions.Statistics));

        // Set to 0.05 to hide the border line at 0 values. Otherwise it would
        // seem that there are actual measure travel data there too.
        Plot.Axes.SetLimits(left: 0.05);

        AddStatistics(telemetryData);
    }

    private TravelStatisticsOptions CreateOptions() => new(AnalysisRange, HistogramMode);

    private static string FormatCount(int count, string singular, string plural) =>
        count == 1 ? $"{count} {singular}" : $"{count} {plural}";
}