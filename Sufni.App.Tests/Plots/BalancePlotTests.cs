using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.PlotTestHelpers;

namespace Sufni.App.Tests.Plots;

public class BalancePlotTests
{
    [Fact]
    public void LoadTelemetryData_InsufficientBalanceSamples_SkipsRendering()
    {
        var telemetry = CreateTelemetryWithSingleBalanceSamplePerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);

        Assert.Empty(plot.PlottableList);
        Assert.True(string.IsNullOrWhiteSpace(plot.Axes.Title.Label.Text));
    }

    [Fact]
    public void LoadTelemetryData_WithBalanceSamples_RendersSlopeDeltaLabel()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);

        var labels = plot.PlottableList.OfType<Text>().SelectMany(ReadTextLabels).ToArray();
        Assert.Contains("zenith", plot.Axes.Title.Label.Text);
        Assert.Contains(labels, label => label.Contains("Slope"));
    }

    [Fact]
    public void LoadTelemetryData_WithBalanceSamples_ShowsFrontRearLegend()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(Alignment.LowerRight, plot.Legend.Alignment);
        Assert.Equal(Color.FromHex("#1A1F23"), plot.Legend.BackgroundColor);
        Assert.Equal(
            ["Front", "Rear"],
            plot.PlottableList
                .OfType<Scatter>()
                .Select(scatter => scatter.LegendText)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray());
    }

    [Fact]
    public void SetPointerPositionWithReadout_ShowsFrontAndRearBalanceValuesAtPointerX()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);
        plot.GetSvgXml(500, 320);
        sut.SetPointerPositionWithReadout(7.5, 150);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.DoesNotContain("Balance", tooltip.LabelText);
        Assert.StartsWith("Zenith: 7.5 %", tooltip.LabelText);
        Assert.Contains("Zenith: 7.5 %", tooltip.LabelText);
        Assert.Contains("Front peak speed: 150 mm/s", tooltip.LabelText);
        Assert.Contains("Rear peak speed: 125 mm/s", tooltip.LabelText);
    }

    [Fact]
    public void SetPointerPositionWithReadout_UsesStrokeTravelLabelInTravelMode()
    {
        var telemetry = CreateTelemetryWithTwoBalanceSamplesPerSide();
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression)
        {
            DisplacementMode = BalanceDisplacementMode.Travel,
        };

        sut.LoadTelemetryData(telemetry);
        plot.GetSvgXml(500, 320);
        sut.SetPointerPositionWithReadout(7.5, 125);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Stroke travel: 7.5 %", tooltip.LabelText);
        Assert.Contains("Front peak speed: 150 mm/s", tooltip.LabelText);
        Assert.Contains("Rear peak speed: 125 mm/s", tooltip.LabelText);
    }

    private static TelemetryData CreateTelemetryWithSingleBalanceSamplePerSide()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry.Front.Strokes = new Strokes
        {
            Compressions =
            [
                new Stroke
                {
                    Start = 0,
                    End = 0,
                    Stat = new StrokeStat
                    {
                        Count = 1,
                        MaxTravel = 10,
                        MaxVelocity = 100,
                    },
                    DigitizedTravel = [0],
                    DigitizedVelocity = [0],
                    FineDigitizedVelocity = [0],
                },
            ],
            Rebounds = [],
        };

        telemetry.Rear.Strokes = new Strokes
        {
            Compressions =
            [
                new Stroke
                {
                    Start = 0,
                    End = 0,
                    Stat = new StrokeStat
                    {
                        Count = 1,
                        MaxTravel = 12,
                        MaxVelocity = 120,
                    },
                    DigitizedTravel = [0],
                    DigitizedVelocity = [0],
                    FineDigitizedVelocity = [0],
                },
            ],
            Rebounds = [],
        };

        return telemetry;
    }

    private static TelemetryData CreateTelemetryWithTwoBalanceSamplesPerSide()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry.Front.Travel = [0, 10, 0, 20];
        telemetry.Front.Strokes = new Strokes
        {
            Compressions =
            [
                CreateStroke(0, 1, maxTravel: 10, maxVelocity: 100),
                CreateStroke(2, 3, maxTravel: 20, maxVelocity: 200),
            ],
            Rebounds = [],
        };

        telemetry.Rear.Travel = [0, 10, 0, 20];
        telemetry.Rear.Strokes = new Strokes
        {
            Compressions =
            [
                CreateStroke(0, 1, maxTravel: 10, maxVelocity: 100),
                CreateStroke(2, 3, maxTravel: 20, maxVelocity: 150),
            ],
            Rebounds = [],
        };

        return telemetry;
    }

    private static Stroke CreateStroke(int start, int end, double maxTravel, double maxVelocity)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                Count = end - start + 1,
                MaxTravel = maxTravel,
                MaxVelocity = maxVelocity,
            },
            DigitizedTravel = [0],
            DigitizedVelocity = [0],
            FineDigitizedVelocity = [0],
        };
    }
}
