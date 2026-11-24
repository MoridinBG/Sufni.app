using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Terrain.Views;

[Collection("Ui")]
public class TerrainClassStripPlotViewTests
{
    [AvaloniaFact]
    public async Task RoughnessAndGradeOccupySeparateTimedBands()
    {
        var view = new TerrainClassStripPlotView();
        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        view.Telemetry = TestTelemetryData.CreateProcessed(sampleRate: 20);
        view.Terrain = new([new(1, 2, RoughnessClass.Smooth), new(2, 4, RoughnessClass.Medium), new(6, 7, RoughnessClass.Rough)],
            [new(1, 4, GradeBand.SteepDescent)], [], [], [], new(1, 0, 0, false, false, false, false, false, []));
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View).Plot;
        var rectangles = plot.PlottableList.OfType<Rectangle>().Select(r => r.GetAxisLimits()).ToArray();
        Assert.Equal([(1d, 2d), (2d, 4d), (6d, 7d)], rectangles.Where(r => r.Bottom == 1 && r.Top == 2)
            .Select(r => (r.Left, r.Right)));
        var lower = Assert.Single(rectangles, r => r.Bottom == 0 && r.Top == 1);
        Assert.Equal((1d, 4d), (lower.Left, lower.Right));
    }
}
