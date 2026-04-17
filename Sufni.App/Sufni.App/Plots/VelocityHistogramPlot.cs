using System.Collections.Generic;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VelocityHistogramPlot(Plot plot, SuspensionType type) : TelemetryPlot(plot)
{
    private const double VelocityLimit = 2000.0;
    private readonly List<Color> palette =
    [
        Color.FromHex("#3288bd"),
        Color.FromHex("#66c2a5"),
        Color.FromHex("#abdda4"),
        Color.FromHex("#e6f598"),
        Color.FromHex("#ffffbf"),
        Color.FromHex("#fee08b"),
        Color.FromHex("#fdae61"),
        Color.FromHex("#f46d43"),
        Color.FromHex("#d53e4f"),
        Color.FromHex("#9e0142"),
    ];

    private void AddStatistics(TelemetryData telemetryData)
    {
        var statistics = telemetryData.CalculateVelocityStatistics(type);

        var maxReboundVelString = $"{statistics.MaxRebound:0.00} mm/s";
        var avgReboundVelString = $"{statistics.AverageRebound:0.00} mm/s";
        var avgCompVelString = $"{statistics.AverageCompression:0.00} mm/s";
        var maxCompVelString = $"{statistics.MaxCompression:0.00} mm/s";

        // TODO: Restore original behaviour: label at bottom when not in range, but moves to its proper
        // place when it is scrolled into view.
        AddLabelWithHorizontalLine(maxReboundVelString, statistics.MaxRebound, LabelLinePosition.Above);

        // Average values should be between the hardcoded limits, it's safe to draw them 
        // at their actual position.
        AddLabelWithHorizontalLine(avgReboundVelString, statistics.AverageRebound, LabelLinePosition.Below);
        AddLabelWithHorizontalLine(avgCompVelString, statistics.AverageCompression, LabelLinePosition.Above);

        // TODO: Restore original behaviour: label at bottom when not in range, but moves to its proper
        // place when it is scrolled into view.
        AddLabelWithHorizontalLine(maxCompVelString, statistics.MaxCompression, LabelLinePosition.Below);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!telemetryData.HasStrokeData(type))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = type == SuspensionType.Front
            ? "Front velocity (time% / mm/s)"
            : "Rear velocity (time% / mm/s)";
        Plot.Layout.Fixed(new PixelPadding(40, 5, 40, 40));

        var data = telemetryData.CalculateVelocityHistogram(type);
        var step = data.Bins[1] - data.Bins[0];

        for (var i = 0; i < data.Values.Count; ++i)
        {
            double nextBarBase = 0;

            for (var j = 0; j < TelemetryData.TravelBinsForVelocityHistogram; j++)
            {
                if (data.Values[i][j] == 0)
                {
                    continue;
                }

                Plot.Add.Bar(new Bar
                {
                    Position = data.Bins[i],
                    ValueBase = nextBarBase,
                    Value = nextBarBase + data.Values[i][j],
                    FillColor = palette[j].WithOpacity(0.8),
                    LineColor = Colors.Black,
                    LineWidth = 0.5f,
                    Orientation = Orientation.Horizontal,
                    Size = step * 0.95
                });

                nextBarBase += data.Values[i][j];
            }
        }

        Plot.Axes.AutoScale(invertY: true);
        var limits = Plot.Axes.GetDataLimits();
        Plot.Axes.AutoScaler = new FixedAutoScaler(minX: 0.1, minY: 2000, maxY: -2000);

        // Lock axes
        Plot.Axes.Rules.Add(new LockedHorizontal(Plot.Axes.Bottom, 0.1, limits.Right / 0.9));

        // Set left axis limit to 0.1 to hide the border line at 0 values. Otherwise
        // it would seem that there are actual measure travel data there too.
        // Also set a hardcoded limit for the velocity range.
        Plot.Axes.SetLimits(left: 0.1,
            bottom: VelocityLimit,
            top: -VelocityLimit);

        Plot.Axes.Left.TickGenerator = new NumericFixedInterval(500);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(2);

        var normalData = telemetryData.CalculateNormalDistribution(type);
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

        AddStatistics(telemetryData);
    }
}