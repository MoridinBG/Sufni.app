using System;
using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VibrationSummaryPlot(Plot plot, SuspensionType type, ImuLocation location) : TelemetryPlot(plot)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var stats = telemetryData.CalculateVibration(location, type);
        if (stats is null)
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var suspensionName = type == SuspensionType.Front ? "Front" : "Rear";
        Plot.Axes.Title.Label.Text = $"{suspensionName} {location} vibration";
        Plot.Layout.Fixed(new PixelPadding(20, 20, 20, 20));
        Plot.HideGrid();
        Plot.Axes.SetLimits(0, 1, 0, 1);

        var summary = string.Join(Environment.NewLine,
            $"Compression: {stats.CompressionPercent:0.0}%    avg {stats.AverageGCompression:0.000} g",
            $"Rebound:     {stats.ReboundPercent:0.0}%    avg {stats.AverageGRebound:0.000} g",
            $"Other:       {stats.OtherPercent:0.0}%",
            $"Overall avg: {stats.AverageGOverall:0.000} g",
            $"Magic carpet: {stats.MagicCarpet:0.000}");

        AddLabel(summary, 0.05, 0.9, 0, 0, Alignment.UpperLeft);
    }
}