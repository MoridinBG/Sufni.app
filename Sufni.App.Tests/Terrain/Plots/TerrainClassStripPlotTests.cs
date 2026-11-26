using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Terrain.Formatting;
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
        new([1, 0, 0, 0], false, false, false, false, false, false, [], 0))
    {
        RearAxle = [new([0.5, 1.5, 2.5], [1.9, 2.1, 2.2])]
    };

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
        Assert.Contains("Rear axle: 2.1 g", text);
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

    [Fact]
    public void LaneDrawsOneRectangleForEachShadeAndOneNameForEachRun()
    {
        var scales = SufniThemes.SignalSeries.Terrain;
        var terrain = Terrain with
        {
            GradeRuns = [],
            RoughnessShades = [new(0, 1, RoughnessClass.Smooth, -1.5), new(1, 4, RoughnessClass.VeryRough, 2),
                new(4, 9, RoughnessClass.VeryRough, 2.5)]
        };
        var plot = new Plot();
        new TerrainClassStripPlot(plot).Load(terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));
        Assert.Equal([scales.ShadeFor(RoughnessClass.Smooth, -1.5).ToScottPlotColor(),
                scales.ShadeFor(RoughnessClass.VeryRough, 2).ToScottPlotColor(), scales.ShadeFor(RoughnessClass.VeryRough, 2.5).ToScottPlotColor()],
            plot.PlottableList.OfType<Rectangle>().Select(r => r.FillColor));
        Assert.Equal(["Smooth", "Very rough"], plot.PlottableList.OfType<Text>().Select(t => t.LabelText).Order());
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void LabelsAddALaneOnTop(bool labelled, double lanes)
    {
        var terrain = labelled ? Terrain with { LabelRuns = [new(4, 6, RoughnessClass.Calm)] } : Terrain;
        var plot = new Plot();
        new TerrainClassStripPlot(plot).Load(terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));

        Assert.Equal(lanes, plot.Axes.GetLimits().Top);
        Assert.Equal(labelled, plot.PlottableList.OfType<Rectangle>()
            .Any(r => r.Y1 >= 2 && r.FillColor == SufniThemes.SignalSeries.Terrain.ColorFor(RoughnessClass.Calm).ToScottPlotColor()));
    }

    [Fact]
    public void ReadoutHoldsTheLabelClassUnderTheCursor()
    {
        var plot = new Plot();
        var strip = new TerrainClassStripPlot(plot);
        strip.Load(Terrain with { LabelRuns = [new(4, 6, RoughnessClass.Calm)] }, TestTelemetryData.CreateProcessed(sampleRate: 20));

        strip.SetCursorPositionWithReadout(5);

        Assert.Contains(TerrainClassText.Name(RoughnessClass.Calm), Assert.Single(plot.PlottableList.OfType<Tooltip>()).LabelText);
    }

    private static string[] VisibleNames(Plot plot) =>
        [.. plot.PlottableList.OfType<Text>().Where(t => t.IsVisible).Select(t => t.LabelText).Order()];
}
