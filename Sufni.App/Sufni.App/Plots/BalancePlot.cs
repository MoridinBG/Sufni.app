using System;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class BalancePlot(Plot plot, BalanceType type) : TelemetryPlot(plot)
{
    private void AddStatistics(TelemetryData telemetryData)
    {
        var balance = telemetryData.CalculateBalance(type);

        var maxVelocity = Math.Max(
            balance.FrontVelocity.Max(),
            balance.RearVelocity.Max());

        var msd = balance.MeanSignedDeviation / maxVelocity * 100.0;
        var msdString = $"MSD: {msd:+0.00;-#.00} %";

        AddLabel(msdString, 100, 0, -10, -5, Alignment.LowerRight);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = type == BalanceType.Compression
           ? "Compression balance (mm/s / travel%)"
           : "Rebound balance (mm/s / travel%)";
        Plot.Layout.Fixed(new PixelPadding(40, 10, 40, 40));

        var balance = telemetryData.CalculateBalance(type);

        var maxVelocity = Math.Max(balance.FrontVelocity.Max(), balance.RearVelocity.Max());
        var roundedMaxVelocity = (int)Math.Ceiling(maxVelocity / 100.0) * 100;
        Plot.Axes.SetLimits(0, 100, 0, roundedMaxVelocity);

        var tickInterval = (int)Math.Ceiling(maxVelocity / 5 / 100.0) * 100;
        Plot.Axes.Left.TickGenerator = new NumericFixedInterval(tickInterval);
        Plot.Axes.Bottom.TickGenerator = new NumericManual(
            [0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 80.0, 90.0, 100.0],
            ["0", "10", "20", "30", "40", "50", "60", "70", "80", "90", "100"]);

        var front = Plot.Add.Scatter(balance.FrontTravel, balance.FrontVelocity);
        front.LineStyle.IsVisible = false;
        front.MarkerStyle.LineColor = FrontColor.WithOpacity();
        front.MarkerStyle.FillColor = FrontColor.WithOpacity();
        front.MarkerStyle.Size = 5;

        var frontTrend = Plot.Add.Scatter(balance.FrontTravel, balance.FrontTrend);
        frontTrend.MarkerStyle.IsVisible = false;
        frontTrend.LineStyle.Color = FrontColor;
        frontTrend.LineStyle.Width = 2;

        var rear = Plot.Add.Scatter(balance.RearTravel, balance.RearVelocity);
        rear.LineStyle.IsVisible = false;
        rear.MarkerStyle.LineColor = RearColor.WithOpacity();
        rear.MarkerStyle.FillColor = RearColor.WithOpacity();
        rear.MarkerStyle.Size = 5;

        var rearTrend = Plot.Add.Scatter(balance.RearTravel, balance.RearTrend);
        rearTrend.MarkerStyle.IsVisible = false;
        rearTrend.LineStyle.Color = RearColor;
        rearTrend.LineStyle.Width = 2;

        AddStatistics(telemetryData);
    }
}