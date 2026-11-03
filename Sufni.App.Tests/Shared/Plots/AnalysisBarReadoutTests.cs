using Sufni.App.Sessions.Analysis.Services;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

using Sufni.App.Sessions.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Shared.Plots;

public class AnalysisBarReadoutTests
{

    [Fact]
    public void TravelDistribution_SetPointerPositionWithReadout_KeepsTooltipAnchorInsideDataArea()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        var data = TelemetryStatistics.CalculateTravelHistogram(telemetry, SuspensionType.Front);
        var index = data.Values.FindIndex(value => value > 0);
        var plot = new Plot();
        var sut = new TravelDistributionPlot(plot, SuspensionType.Front);

        sut.LoadAnalysisData(RecordedSessionAnalysisComputer.CalculateTravelDistribution(
            PlotTestHelpers.CreateAnalysisKey(RecordedSessionAnalysisFamily.TravelDistribution, travelMode: sut.HistogramMode), telemetry));
        plot.GetSvgXml(500, 320);
        var dataRect = plot.LastRender.DataRect;

        sut.SetPointerPositionWithReadout(data.Values[index] / 2.0, data.Bins[index]);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        var labelPixel = plot.GetPixel(tooltip.LabelLocation);
        Assert.InRange(labelPixel.X, Math.Min(dataRect.Left, dataRect.Right), Math.Max(dataRect.Left, dataRect.Right));
        Assert.InRange(labelPixel.Y, Math.Min(dataRect.Top, dataRect.Bottom), Math.Max(dataRect.Top, dataRect.Bottom));
    }

}
