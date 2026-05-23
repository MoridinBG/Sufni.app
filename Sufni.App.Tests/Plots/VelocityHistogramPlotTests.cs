using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.PlotTestHelpers;

namespace Sufni.App.Tests.Plots;

public class VelocityHistogramPlotTests
{
    [Fact]
    public void LoadTelemetryData_WithoutCompressionStrokes_SkipsCompressionPercentileLabel()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        telemetry.Front.Strokes.Compressions = [];
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);

        var labels = plot.PlottableList.OfType<Text>().SelectMany(ReadTextLabels).ToArray();
        Assert.Contains(labels, label => label.Contains("95%") && label.Contains('-'));
        Assert.DoesNotContain(labels, label => label.Contains("95%: 0.0"));
    }

    private static TelemetryData CreateVelocityTelemetry()
    {
        var front = CreateSuspension(
            travel: [0, 10, 20, 10, 0],
            velocity: [100, 120, 80, -60, -90],
            compressions:
            [
                CreateStroke(
                    0,
                    2,
                    maxVelocity: 120,
                    maxTravel: 20,
                    sumVelocity: 300,
                    digitizedTravel: [0, 2, 4],
                    digitizedVelocity: [3, 3, 2]),
            ],
            rebounds:
            [
                CreateStroke(
                    3,
                    4,
                    maxVelocity: -90,
                    maxTravel: 10,
                    sumVelocity: -150,
                    digitizedTravel: [2, 0],
                    digitizedVelocity: [1, 1]),
            ]);

        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 1000, Duration = 0.005 },
            Front = front,
            Rear = CreateEmptySuspension(),
            Airtimes = [],
            Markers = [],
        };
    }

    private static Suspension CreateSuspension(
        double[] travel,
        double[] velocity,
        Stroke[] compressions,
        Stroke[] rebounds)
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = 100,
            Travel = travel,
            Velocity = velocity,
            TravelBins = HistogramBuilder.Linspace(0, 100, Parameters.TravelHistBins + 1),
            VelocityBins = [-200, -100, 0, 100, 200],
            FineVelocityBins = [-200, -100, 0, 100, 200],
            Strokes = new Strokes
            {
                Compressions = compressions,
                Rebounds = rebounds,
            },
        };
    }

    private static Suspension CreateEmptySuspension()
    {
        return new Suspension
        {
            Present = false,
            Travel = [],
            Velocity = [],
            TravelBins = [0, 1],
            VelocityBins = [0, 1],
            FineVelocityBins = [0, 1],
            Strokes = new Strokes
            {
                Compressions = [],
                Rebounds = [],
            },
        };
    }

    private static Stroke CreateStroke(
        int start,
        int end,
        double maxVelocity,
        double maxTravel,
        double sumVelocity,
        int[] digitizedTravel,
        int[] digitizedVelocity)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                SumVelocity = sumVelocity,
                MaxVelocity = maxVelocity,
                MaxTravel = maxTravel,
                Count = end - start + 1,
            },
            DigitizedTravel = digitizedTravel,
            DigitizedVelocity = digitizedVelocity,
            FineDigitizedVelocity = [],
        };
    }

    private static (int VelocityIndex, int TravelIndex) FindStackedValue(StackedHistogramData data)
    {
        for (var velocityIndex = 0; velocityIndex < data.Values.Count; velocityIndex++)
        {
            for (var travelIndex = 0; travelIndex < data.Values[velocityIndex].Length; travelIndex++)
            {
                if (data.Values[velocityIndex][travelIndex] > 0)
                {
                    return (velocityIndex, travelIndex);
                }
            }
        }

        throw new InvalidOperationException("Expected a non-empty stacked histogram.");
    }
}
