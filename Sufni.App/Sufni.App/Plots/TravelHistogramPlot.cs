using System.Linq;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelHistogramPlot(Plot plot, SuspensionType type) : TelemetryPlot(plot)
{
    private void AddStatistics(TelemetryData telemetryData)
    {
        var statistics = telemetryData.CalculateTravelStatistics(type, AnalysisRange);

        var mx = type == SuspensionType.Front
            ? telemetryData.Front.MaxTravel
            : telemetryData.Rear.MaxTravel;
        var avgPercentage = statistics.Average / mx * 100.0;
        var maxPercentage = statistics.Max / mx * 100.0;

        var avgString = $"{statistics.Average:F1} mm ({avgPercentage:F1}%)";
        var maxString = $"{statistics.Max:F1} mm ({maxPercentage:F1}%) / {statistics.Bottomouts} bottom outs";

        AddLabelWithHorizontalLine(avgString, statistics.Average, LabelLinePosition.Above);
        AddLabelWithHorizontalLine(maxString, statistics.Max, LabelLinePosition.Below);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!telemetryData.HasStrokeData(type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = type == SuspensionType.Front
            ? "Front travel (time% / mm)"
            : "Rear travel (time% / mm)";
        Plot.Layout.Fixed(new PixelPadding(40, 10, 40, 40));

        var data = telemetryData.CalculateTravelHistogram(type, AnalysisRange);
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
}