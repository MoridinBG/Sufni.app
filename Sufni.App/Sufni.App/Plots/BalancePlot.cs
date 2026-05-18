using System;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class BalancePlot(Plot plot, BalanceType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    public BalanceDisplacementMode DisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
    public BalanceSpeedMode SpeedMode { get; set; } = BalanceSpeedMode.Both;

    private void AddStatistics(BalanceData balance, double axisMaximum)
    {
        var maxVelocity = Math.Max(
            balance.FrontVelocity.Max(),
            balance.RearVelocity.Max());

        var msd = balance.MeanSignedDeviation / maxVelocity * 100.0;
        var slopeString = $"Slope Δ: {balance.SignedSlopeDeltaPercent:+0.0;-#.0} %";
        var msdString = $"MSD: {msd:+0.0;-#.0} %";
        var labelY = axisMaximum * 0.10;

        AddLabel(slopeString, 100, labelY, -10, -23, Alignment.LowerRight);
        AddLabel(msdString, 100, labelY, -10, -5, Alignment.LowerRight);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var balance = TelemetryStatistics.CalculateBalance(telemetryData, type, CreateOptions());
        if (!HasRenderableBalanceData(balance)) return;

        base.LoadTelemetryData(telemetryData);

        var modeLabel = $"{DisplacementModeLabel()} / {SpeedModeLabel()}";
        var xAxisLabel = DisplacementMode == BalanceDisplacementMode.Travel ? "Stroke travel (%)" : "Zenith (%)";
        var xReadoutLabel = DisplacementMode == BalanceDisplacementMode.Travel ? "Stroke travel" : "Zenith";
        Plot.Axes.Title.Label.Text = type == BalanceType.Compression
              ? $"Compression balance - {modeLabel}"
              : $"Rebound balance - {modeLabel}";
        SetAxisLabels(xAxisLabel, "Peak speed (mm/s)");
        Plot.Layout.Fixed(new PixelPadding(65, 10, 55, 40));

        var maxVelocity = Math.Max(balance.FrontVelocity.Max(), balance.RearVelocity.Max());
        var roundedMaxVelocity = (int)Math.Ceiling(maxVelocity / 100.0) * 100;
        Plot.Axes.SetLimits(0, 100, 0, roundedMaxVelocity);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0, 100, 0, roundedMaxVelocity, ZoomFractions.Statistics));

        var tickInterval = (int)Math.Ceiling(maxVelocity / 5 / 100.0) * 100;
        Plot.Axes.Left.TickGenerator = new NumericFixedInterval(tickInterval);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(10);

        var front = Plot.Add.Scatter(balance.FrontTravel, balance.FrontVelocity);
        front.LineStyle.IsVisible = false;
        front.MarkerStyle.LineColor = FrontColor.WithOpacity();
        front.MarkerStyle.FillColor = FrontColor.WithOpacity();
        front.MarkerStyle.Size = 5;

        var frontTrend = Plot.Add.Scatter(balance.FrontTravel, balance.FrontTrend);
        frontTrend.LegendText = "Front";
        frontTrend.MarkerStyle.IsVisible = false;
        frontTrend.LineStyle.Color = FrontColor;
        frontTrend.LineStyle.Width = 2;

        var rear = Plot.Add.Scatter(balance.RearTravel, balance.RearVelocity);
        rear.LineStyle.IsVisible = false;
        rear.MarkerStyle.LineColor = RearColor.WithOpacity();
        rear.MarkerStyle.FillColor = RearColor.WithOpacity();
        rear.MarkerStyle.Size = 5;

        var rearTrend = Plot.Add.Scatter(balance.RearTravel, balance.RearTrend);
        rearTrend.LegendText = "Rear";
        rearTrend.MarkerStyle.IsVisible = false;
        rearTrend.LineStyle.Color = RearColor;
        rearTrend.LineStyle.Width = 2;

        AddPointerReadoutTarget(BalanceTrendReadout.FromTrends(
            balance.FrontTravel,
            balance.FrontTrend,
            balance.RearTravel,
            balance.RearTrend,
            xReadoutLabel));

        ShowSourceLegend();
        AddStatistics(balance, roundedMaxVelocity);
    }

    private BalanceStatisticsOptions CreateOptions() => new(AnalysisRange, DisplacementMode, SpeedMode);

    private string DisplacementModeLabel() => DisplacementMode == BalanceDisplacementMode.Travel ? "travel" : "zenith";

    private string SpeedModeLabel()
    {
        return SpeedMode switch
        {
            BalanceSpeedMode.LowSpeed => "low speed",
            BalanceSpeedMode.HighSpeed => "high speed",
            _ => "all speeds",
        };
    }

    private static bool HasRenderableBalanceData(BalanceData balance) =>
        balance.FrontTravel.Count >= 2 && balance.RearTravel.Count >= 2;
}
