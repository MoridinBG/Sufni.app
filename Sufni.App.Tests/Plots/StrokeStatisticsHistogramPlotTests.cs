using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Plots;

public class StrokeStatisticsHistogramPlotTests
{
    [Fact]
    public void StrokeSpeedHistogram_RendersOnlyBinsWithStrokeValues()
    {
        var telemetry = CreateStrokeTelemetry(
            [
                CreateStroke(0, 1, maxVelocity: 120, maxTravel: 20),
                CreateStroke(2, 3, maxVelocity: 360, maxTravel: 45),
            ]);
        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var plot = new Plot();
        var sut = new StrokeSpeedHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);

        Assert.Contains(data.Values, value => value == 0);
        var bars = GetRenderedBars(plot);
        Assert.Equal(data.Values.Count(value => value > 0), bars.Length);
        Assert.All(bars, bar => Assert.True(bar.Value > 0));
    }

    [Fact]
    public void StrokeLengthHistogram_RendersOnlyBinsWithStrokeValues()
    {
        var telemetry = CreateStrokeTelemetry(
            [
                CreateStroke(0, 1, maxVelocity: 120, maxTravel: 20),
                CreateStroke(2, 3, maxVelocity: 360, maxTravel: 45),
            ]);
        var data = TelemetryStatistics.CalculateStrokeLengthHistogram(
            telemetry,
            SuspensionType.Front,
            BalanceType.Compression);
        var plot = new Plot();
        var sut = new StrokeLengthHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);

        sut.LoadTelemetryData(telemetry);

        Assert.Contains(data.Values, value => value == 0);
        var bars = GetRenderedBars(plot);
        Assert.Equal(data.Values.Count(value => value > 0), bars.Length);
        Assert.All(bars, bar => Assert.True(bar.Value > 0));
    }

    [Fact]
    public void DeepTravelHistogram_RendersOnlyBinsWithStrokeValues()
    {
        var telemetry = CreateStrokeTelemetry(
            [
                CreateStroke(0, 1, maxVelocity: 120, maxTravel: 80),
                CreateStroke(2, 3, maxVelocity: 360, maxTravel: 170),
            ]);
        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);
        var plot = new Plot();
        var sut = new DeepTravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);

        Assert.Contains(data.Values, value => value == 0);
        var bars = GetRenderedBars(plot);
        Assert.Equal(data.Values.Count(value => value > 0), bars.Length);
        Assert.All(bars, bar => Assert.True(bar.Value > 0));
    }

    [Fact]
    public void DeepTravelHistogram_DoesNotRenderBarPlot_WhenNoBinsHaveStrokeValues()
    {
        var telemetry = CreateStrokeTelemetry(
            [
                CreateStroke(0, 1, maxVelocity: 120, maxTravel: 80),
            ]);
        var data = TelemetryStatistics.CalculateDeepTravelHistogram(telemetry, SuspensionType.Front);
        var plot = new Plot();
        var sut = new DeepTravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);

        Assert.All(data.Values, value => Assert.Equal(0, value));
        Assert.Empty(plot.PlottableList.OfType<BarPlot>());
    }

    private static Bar[] GetRenderedBars(Plot plot)
    {
        var barPlot = Assert.Single(plot.PlottableList.OfType<BarPlot>());
        return barPlot.Bars.ToArray();
    }

    private static TelemetryData CreateStrokeTelemetry(Stroke[] compressions)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                SampleRate = 1000,
                Duration = 0.004,
            },
            Front = CreateSuspension(compressions),
            Rear = CreateEmptySuspension(),
            Airtimes = [],
            Markers = [],
        };
    }

    private static Suspension CreateSuspension(Stroke[] compressions)
    {
        return new Suspension
        {
            Present = true,
            MaxTravel = 200,
            Travel = [0, 20, 40, 60],
            Velocity = [120, 0, 360, 0],
            TravelBins = HistogramBuilder.Linspace(0, 200, Parameters.TravelHistBins + 1),
            VelocityBins = [-400, -300, -200, -100, 0, 100, 200, 300, 400],
            FineVelocityBins = [-400, -300, -200, -100, 0, 100, 200, 300, 400],
            Strokes = new Strokes
            {
                Compressions = compressions,
                Rebounds = [],
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

    private static Stroke CreateStroke(int start, int end, double maxVelocity, double maxTravel)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                MaxVelocity = maxVelocity,
                MaxTravel = maxTravel,
                Count = end - start + 1,
            },
            DigitizedTravel = [],
            DigitizedVelocity = [],
            FineDigitizedVelocity = [],
        };
    }
}
