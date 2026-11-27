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
    private static readonly string[] LaneNames = ["Roughness", "Roughness labels", "Grade", "Steepness labels"];
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
        Assert.Equal(["Smooth", "Very rough"], RunNames(plot).Select(t => t.LabelText).Order());
    }

    [Theory]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 4)]
    public void EachKindOfLabelsAddsALane(bool roughnessLabelled, bool steepnessLabelled, double lanes)
    {
        var terrain = Terrain with
        {
            RoughnessLabelRuns = roughnessLabelled ? [new(4, 6, RoughnessClass.Calm)] : [],
            SteepnessLabelRuns = steepnessLabelled ? [new(4, 6, GradeBand.Climb)] : []
        };
        var plot = new Plot();
        new TerrainClassStripPlot(plot).Load(terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));

        Assert.Equal(lanes, plot.Axes.GetLimits().Top);
    }

    [Fact]
    public void EachLabelLaneSitsAboveTheLaneItLabels()
    {
        var scales = SufniThemes.SignalSeries.Terrain;
        var terrain = Terrain with
        {
            RoughnessLabelRuns = [new(4, 6, RoughnessClass.Calm)],
            SteepnessLabelRuns = [new(4, 6, GradeBand.Climb)]
        };
        var plot = new Plot();
        new TerrainClassStripPlot(plot).Load(terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));

        var rectangles = plot.PlottableList.OfType<Rectangle>().ToArray();
        Assert.Equal(1, Lane(rectangles.Single(r => r.FillColor == scales.ColorFor(RoughnessClass.Calm).ToScottPlotColor())));
        Assert.Equal(3, Lane(rectangles.Single(r => r.FillColor == scales.ColorFor(GradeBand.Climb).ToScottPlotColor())));
    }

    [Fact]
    public void LaneNamesFollowTheLeftEdge()
    {
        var plot = new Plot();
        var strip = new TerrainClassStripPlot(plot);
        strip.Load(Terrain, TestTelemetryData.CreateProcessed(sampleRate: 20));

        strip.LayoutRunNames(5, 9, 300);

        Assert.All(plot.PlottableList.OfType<Text>().Where(t => LaneNames.Contains(t.LabelText)), name => Assert.Equal(5, name.Location.X));
    }

    [Fact]
    public void ReadoutHoldsTheLabelClassesUnderTheCursor()
    {
        var plot = new Plot();
        var strip = new TerrainClassStripPlot(plot);
        strip.Load(Terrain with
        {
            RoughnessLabelRuns = [new(4, 6, RoughnessClass.Calm)],
            SteepnessLabelRuns = [new(4, 6, GradeBand.Climb)]
        }, TestTelemetryData.CreateProcessed(sampleRate: 20));

        strip.SetCursorPositionWithReadout(5);

        var text = Assert.Single(plot.PlottableList.OfType<Tooltip>()).LabelText;
        Assert.Contains(TerrainClassText.Name(RoughnessClass.Calm), text);
        Assert.Contains(TerrainClassText.Name(GradeBand.Climb), text);
    }

    private static string[] VisibleNames(Plot plot) => [.. RunNames(plot).Where(t => t.IsVisible).Select(t => t.LabelText).Order()];

    private static IEnumerable<Text> RunNames(Plot plot) => plot.PlottableList.OfType<Text>().Where(t => !LaneNames.Contains(t.LabelText));

    private static int Lane(Rectangle rectangle) => (int)Math.Floor(rectangle.Y1);
}
