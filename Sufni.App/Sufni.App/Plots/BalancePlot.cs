using System;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.App.SessionDetails;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class BalancePlot(Plot plot, BalanceType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    public BalanceDisplacementMode DisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
    public BalanceSpeedMode SpeedMode { get; set; } = BalanceSpeedMode.Both;
    public DampingSpeedCutoffs DampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;

    private bool UsesTrend => DisplacementMode != BalanceDisplacementMode.Speed;

    private void AddStatistics(BalanceData balance)
    {
        var maxVelocity = Math.Max(
            balance.FrontVelocity.Max(),
            balance.RearVelocity.Max());

        var msd = balance.MeanSignedDeviation / maxVelocity * 100.0;
        var slopeString = $"Δ {balance.SignedSlopeDeltaPercent:+0.0;-#.0}%";
        var msdString = $"MSD {msd:+0.0;-#.0}%";

        AddLabel($"{slopeString}   {msdString}", 100, 0, -10, -30, Alignment.LowerRight);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var balance = TelemetryStatistics.CalculateBalance(telemetryData, type, CreateOptions());
        if (!HasRenderableBalanceData(balance)) return;

        base.LoadTelemetryData(telemetryData);

        var xAxisLabel = DisplacementMode switch
        {
            BalanceDisplacementMode.Travel => "Stroke travel (%)",
            BalanceDisplacementMode.Speed => "Peak speed position (%)",
            _ => "Zenith (%)",
        };
        var xReadoutLabel = DisplacementMode switch
        {
            BalanceDisplacementMode.Travel => "Stroke travel",
            BalanceDisplacementMode.Speed => "Peak speed position",
            _ => "Zenith",
        };
        SetTitle(StatisticsPlotTitles.Balance(type, DisplacementMode, SpeedMode));
        SetAxisLabels(xAxisLabel, "Peak speed (mm/s)");
        Plot.Layout.Fixed(CreateStatisticsPlotPadding());

        var maxVelocity = Math.Max(balance.FrontVelocity.Max(), balance.RearVelocity.Max());
        var roundedMaxVelocity = (int)Math.Ceiling(maxVelocity / 100.0) * 100;
        Plot.Axes.SetLimits(0, 100, 0, roundedMaxVelocity);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0, 100, 0, roundedMaxVelocity, ZoomFractions.Statistics));

        var tickInterval = (int)Math.Ceiling(maxVelocity / 5 / 100.0) * 100;
        Plot.Axes.Left.TickGenerator = new NumericFixedInterval(tickInterval);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(10);

        var front = Plot.Add.Scatter(balance.FrontTravel, balance.FrontVelocity);
        if (!UsesTrend)
        {
            front.LegendText = "Front";
        }
        front.LineStyle.IsVisible = false;
        front.MarkerStyle.LineColor = FrontColor.WithOpacity();
        front.MarkerStyle.FillColor = FrontColor.WithOpacity();
        front.MarkerStyle.Size = 5;

        var rear = Plot.Add.Scatter(balance.RearTravel, balance.RearVelocity);
        if (!UsesTrend)
        {
            rear.LegendText = "Rear";
        }
        rear.LineStyle.IsVisible = false;
        rear.MarkerStyle.LineColor = RearColor.WithOpacity();
        rear.MarkerStyle.FillColor = RearColor.WithOpacity();
        rear.MarkerStyle.Size = 5;

        if (UsesTrend)
        {
            var frontTrend = Plot.Add.Scatter(balance.FrontTravel, balance.FrontTrend);
            frontTrend.LegendText = "Front";
            frontTrend.MarkerStyle.IsVisible = false;
            frontTrend.LineStyle.Color = FrontColor;
            frontTrend.LineStyle.Width = 2;

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
            AddStatistics(balance);
        }
        else
        {
            AddPointReadouts(balance, xReadoutLabel);
        }

        ShowSourceLegend();
        Plot.Legend.Orientation = Orientation.Horizontal;
        Plot.Legend.FontSize = 10;
        Plot.Legend.SymbolHeight = 12;
        Plot.Legend.Padding = new PixelPadding(4, 4, 4, 4);
    }

    private BalanceStatisticsOptions CreateOptions() => new(
        AnalysisRange,
        DisplacementMode,
        SpeedMode,
        DampingSpeedCutoffs.Front.CompressionMmPerSecond,
        DampingSpeedCutoffs.Front.ReboundMmPerSecond,
        DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
        DampingSpeedCutoffs.Rear.ReboundMmPerSecond);

    private void AddPointReadouts(BalanceData balance, string xReadoutLabel)
    {
        for (var index = 0; index < balance.FrontTravel.Count && index < balance.FrontVelocity.Count; index++)
        {
            AddPointerReadoutTarget(BalancePointReadout.FromPoint(
                balance.FrontTravel[index],
                balance.FrontVelocity[index],
                "Front",
                xReadoutLabel,
                FrontColor));
        }

        for (var index = 0; index < balance.RearTravel.Count && index < balance.RearVelocity.Count; index++)
        {
            AddPointerReadoutTarget(BalancePointReadout.FromPoint(
                balance.RearTravel[index],
                balance.RearVelocity[index],
                "Rear",
                xReadoutLabel,
                RearColor));
        }
    }

    private static bool HasRenderableBalanceData(BalanceData balance) =>
        balance.FrontTravel.Count >= 2 && balance.RearTravel.Count >= 2;
}
