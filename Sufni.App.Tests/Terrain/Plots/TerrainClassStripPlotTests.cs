using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Plots;

public class TerrainClassStripPlotTests
{
    private static readonly TerrainPresentation Terrain = new(
        [new(0, 1, RoughnessClass.Smooth), new(1, 9, RoughnessClass.VeryRough)],
        [new(0, 9, GradeBand.SteepDescent)],
        [new([0.5, 1.5, 2.5], [0.8, 3.6, 3.7])], [], [new([0.5, 1.5, 2.5], [11, 12.4, 13])],
        new(1, 0, 0, false, false, false, false, false, []));

    [Fact]
    public void ReadoutNamesTheBandAndLevelUnderTheCursorWithTheMeasuredValues()
    {
        var plot = new Plot();
        var strip = new TerrainClassStripPlot(plot);
        strip.Load(Terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));
        strip.SetCursorPositionWithReadout(1.4);
        var text = Assert.Single(plot.PlottableList.OfType<Tooltip>()).LabelText;
        Assert.Contains("Grade: Steep descent · 12.4°", text);
        Assert.Contains("Roughness: Very rough", text);
        Assert.Contains("Frame: 3.6 g", text);
    }

    [Fact]
    public void RunNamesShowOnlyWhereTheyFitTheVisibleRun()
    {
        var plot = new Plot();
        var strip = new TerrainClassStripPlot(plot);
        strip.Load(Terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));
        strip.LayoutRunNames(0, 9, 300);
        Assert.Equal(["Steep descent", "Very rough"], VisibleNames(plot));
        strip.LayoutRunNames(0, 9, 60);
        Assert.Empty(VisibleNames(plot));
    }

    private static string[] VisibleNames(Plot plot) =>
        [.. plot.PlottableList.OfType<Text>().Where(t => t.IsVisible).Select(t => t.LabelText).Order()];
}
