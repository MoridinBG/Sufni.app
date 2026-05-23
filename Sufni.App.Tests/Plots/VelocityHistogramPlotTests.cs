using System.Collections.Generic;
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
    public void LoadTelemetryData_RendersPercentileLabels()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);

        var labels = plot.PlottableList.OfType<Text>().SelectMany(ReadTextLabels).ToArray();
        Assert.Contains("sample-averaged stats", plot.Axes.Title.Label.Text);
        Assert.Contains(labels, label => label.Contains("95%"));
    }

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

    [Fact]
    public void LoadTelemetryData_WithStrokePeakMode_LabelsHistogramDenominatorAsStrokes()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front)
        {
            AverageMode = VelocityAverageMode.StrokePeakAveraged,
        };

        sut.LoadTelemetryData(telemetry);

        Assert.Contains("stroke-peak stats", plot.Axes.Title.Label.Text);
        Assert.DoesNotContain("(", plot.Axes.Title.Label.Text);
    }

}
