using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Plots;

public class TravelHistogramPlotTests
{
    [Fact]
    public void LoadTelemetryData_WithoutStrokeData_SkipsRendering()
    {
        var telemetry = CreateTelemetryWithoutStrokes();
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);

        Assert.Empty(plot.PlottableList);
        Assert.True(string.IsNullOrWhiteSpace(plot.Axes.Title.Label.Text));
    }

    [Fact]
    public void LoadTelemetryData_WithDynamicSagMode_RendersWithoutStrokeData()
    {
        var telemetry = CreateTelemetryWithoutStrokes();
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front)
        {
            HistogramMode = TravelHistogramMode.DynamicSag,
        };

        sut.LoadTelemetryData(telemetry);

        Assert.NotEmpty(plot.PlottableList);
        Assert.Contains("dynamic sag", plot.Axes.Title.Label.Text);
    }

    [Theory]
    [InlineData(TravelHistogramMode.ActiveSuspension, "stroke bottom-outs")]
    [InlineData(TravelHistogramMode.DynamicSag, "bottom-out regions")]
    public void LoadTelemetryData_LabelsBottomoutsByHistogramMode(
        TravelHistogramMode histogramMode,
        string expectedLabel)
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front)
        {
            HistogramMode = histogramMode,
        };

        sut.LoadTelemetryData(telemetry);

        var labels = plot.PlottableList.OfType<Text>().SelectMany(ReadTextLabels).ToArray();
        Assert.Contains(labels, label => label.Contains(expectedLabel));
    }

    private static IEnumerable<string> ReadTextLabels(Text text)
    {
        return text.GetType()
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(text) as string)
            .Where(label => !string.IsNullOrWhiteSpace(label))!;
    }

    private static TelemetryData CreateTelemetryWithoutStrokes()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

        telemetry.Front.Strokes = new Strokes
        {
            Compressions = [],
            Rebounds = [],
        };

        telemetry.Rear.Strokes = new Strokes
        {
            Compressions = [],
            Rebounds = [],
        };

        return telemetry;
    }
}