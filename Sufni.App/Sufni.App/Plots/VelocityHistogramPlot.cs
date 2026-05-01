using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VelocityHistogramPlot(Plot plot, SuspensionType type) : TelemetryPlot(plot)
{
    private const double VelocityLimit = 2000.0;
    private static readonly IReadOnlyList<Color> palette =
        TravelZonePalette.HexColors.Select(Color.FromHex).ToArray();

    public VelocityAverageMode AverageMode { get; set; } = VelocityAverageMode.SampleAveraged;

    private void AddStatistics(VelocityStatistics statistics)
    {
        var maxReboundVelString = $"{statistics.MaxRebound:0.0} mm/s";
        var percentileReboundString = $"95%: {statistics.Percentile95Rebound:0.0} mm/s";
        var avgReboundVelString = $"{statistics.AverageRebound:0.0} mm/s";
        var avgCompVelString = $"{statistics.AverageCompression:0.0} mm/s";
        var percentileCompString = $"95%: {statistics.Percentile95Compression:0.0} mm/s";
        var maxCompVelString = $"{statistics.MaxCompression:0.0} mm/s";

        // TODO: Restore original behaviour: label at bottom when not in range, but moves to its proper
        // place when it is scrolled into view.
        AddLabelWithHorizontalLine(maxReboundVelString, statistics.MaxRebound, LabelLinePosition.Above);
        if (statistics.ReboundStrokeCount > 0)
        {
            AddLabelWithHorizontalLine(percentileReboundString, statistics.Percentile95Rebound, LabelLinePosition.Above);
        }

        // Average values should be between the hardcoded limits, it's safe to draw them 
        // at their actual position.
        AddLabelWithHorizontalLine(avgReboundVelString, statistics.AverageRebound, LabelLinePosition.Below);
        AddLabelWithHorizontalLine(avgCompVelString, statistics.AverageCompression, LabelLinePosition.Above);
        if (statistics.CompressionStrokeCount > 0)
        {
            AddLabelWithHorizontalLine(percentileCompString, statistics.Percentile95Compression, LabelLinePosition.Below);
        }

        // TODO: Restore original behaviour: label at bottom when not in range, but moves to its proper
        // place when it is scrolled into view.
        AddLabelWithHorizontalLine(maxCompVelString, statistics.MaxCompression, LabelLinePosition.Below);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!telemetryData.HasStrokeData(type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var isStrokePeakMode = AverageMode == VelocityAverageMode.StrokePeakAveraged;
        var modeLabel = isStrokePeakMode ? "stroke-peak stats" : "sample-averaged stats";
        var percentageLabel = isStrokePeakMode ? "stroke%" : "time%";
        Plot.Axes.Title.Label.Text = type == SuspensionType.Front
            ? $"Front velocity - {modeLabel} ({percentageLabel} / mm/s)"
            : $"Rear velocity - {modeLabel} ({percentageLabel} / mm/s)";
        Plot.Layout.Fixed(new PixelPadding(40, 5, 40, 40));

        var data = telemetryData.CalculateVelocityHistogram(type, CreateOptions());
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

        // Y bounds must include the max-compression/-rebound stats labels, which can sit
        // outside the hardcoded ±VelocityLimit display window.
        var velocityStats = telemetryData.CalculateVelocityStatistics(type, CreateOptions());
        var yLow = Math.Min(-VelocityLimit, velocityStats.MaxRebound);
        if (velocityStats.ReboundStrokeCount > 0)
        {
            yLow = Math.Min(yLow, velocityStats.Percentile95Rebound);
        }

        var yHigh = Math.Max(VelocityLimit, velocityStats.MaxCompression);
        if (velocityStats.CompressionStrokeCount > 0)
        {
            yHigh = Math.Max(yHigh, velocityStats.Percentile95Compression);
        }

        // Lock axes
        Plot.Axes.Rules.Add(new LockedHorizontal(Plot.Axes.Bottom, 0.1, limits.Right / 0.9));
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0.1, limits.Right / 0.9, yLow, yHigh, ZoomFractions.Statistics));

        // Set left axis limit to 0.1 to hide the border line at 0 values. Otherwise
        // it would seem that there are actual measure travel data there too.
        // Also set a hardcoded limit for the velocity range.
        Plot.Axes.SetLimits(left: 0.1,
            bottom: VelocityLimit,
            top: -VelocityLimit);

        Plot.Axes.Left.TickGenerator = new NumericFixedInterval(500);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(2);

        var normalData = AverageMode == VelocityAverageMode.SampleAveraged
            ? telemetryData.CalculateNormalDistribution(type, AnalysisRange)
            : new NormalDistributionData([], []);
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

        AddStatistics(velocityStats);
    }

    private VelocityStatisticsOptions CreateOptions() => new(AnalysisRange, AverageMode);
}