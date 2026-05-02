using System.Collections.Generic;
using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VibrationThirdsPlot(Plot plot, SuspensionType type, ImuLocation location) : TelemetryPlot(plot)
{
    private static readonly Color lowerColor = Color.FromHex(TravelZonePalette.HexColors[1]);
    private static readonly Color middleColor = Color.FromHex(TravelZonePalette.HexColors[4]);
    private static readonly Color upperColor = Color.FromHex(TravelZonePalette.HexColors[8]);

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var stats = TelemetryStatistics.CalculateVibration(telemetryData, location, type, AnalysisRange);
        if (stats is null)
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var suspensionName = type == SuspensionType.Front ? "Front" : "Rear";
        Plot.Axes.Title.Label.Text = $"{suspensionName} {location} vibration thirds";
        SetAxisLabels("Stroke group", "Vibration (%)");
        Plot.Layout.Fixed(new PixelPadding(65, 10, 55, 45));

        var groups = new[]
        {
            stats.CompressionThirds,
            stats.ReboundThirds,
            stats.OverallThirds,
        };
        var colors = new[] { lowerColor, middleColor, upperColor };
        var bars = new List<Bar>();
        const double barWidth = 0.22;

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var values = new[] { groups[groupIndex].Lower, groups[groupIndex].Middle, groups[groupIndex].Upper };
            for (var thirdIndex = 0; thirdIndex < values.Length; thirdIndex++)
            {
                bars.Add(new Bar
                {
                    Position = groupIndex + (thirdIndex - 1) * barWidth,
                    Value = values[thirdIndex],
                    FillColor = colors[thirdIndex].WithOpacity(),
                    LineColor = colors[thirdIndex],
                    LineWidth = 1.5f,
                    Orientation = Orientation.Vertical,
                    Size = barWidth * 0.85,
                });
            }
        }

        Plot.Add.Bars(bars);
        Plot.Axes.SetLimits(left: -0.5, right: 2.5, bottom: 0, top: 100);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            -0.5, 2.5, 0, 100, ZoomFractions.Statistics));
        Plot.Axes.Bottom.SetTicks([0, 1, 2], ["Compression", "Rebound", "Overall"]);
    }
}