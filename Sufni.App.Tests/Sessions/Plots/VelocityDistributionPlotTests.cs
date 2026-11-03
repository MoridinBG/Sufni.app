using Sufni.App.Sessions.Analysis.Services;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;

using Sufni.App.Sessions.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Plots;

public class VelocityDistributionPlotTests
{
    [Fact]
    public void LoadAnalysisData_WithoutCompressionStrokes_SkipsCompressionPercentileLabel()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        telemetry = telemetry with { Front = telemetry.Front with { Strokes = telemetry.Front.Strokes with { Compressions = [] } } };
        var plot = new Plot();
        var sut = new VelocityDistributionPlot(plot, SuspensionType.Front);

        sut.LoadAnalysisData(RecordedSessionAnalysisComputer.CalculateVelocityDistribution(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.VelocityDistribution), telemetry));

        var labels = plot.PlottableList.OfType<Text>().SelectMany(ReadTextLabels).ToArray();
        Assert.Contains(labels, label => label.Contains("95%") && label.Contains('-'));
        Assert.DoesNotContain(labels, label => label.Contains("95%: 0.0"));
    }

}
