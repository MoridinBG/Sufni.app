using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

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

    private static IEnumerable<string> ReadTextLabels(Text text)
    {
        return text.GetType()
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(text) as string)
            .Where(label => !string.IsNullOrWhiteSpace(label))!;
    }

    private static TelemetryData CreateTelemetryWithSingleBalanceSamplePerSide()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

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
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

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