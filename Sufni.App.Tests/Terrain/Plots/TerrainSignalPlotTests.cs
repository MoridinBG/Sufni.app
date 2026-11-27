using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Shared.Plots;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Plots;

public class TerrainSignalPlotTests
{
    private static readonly TerrainPresentation Terrain = new(
        [new(0, 1, RoughnessClass.Calm), new(1, 3, RoughnessClass.Rough)],
        [new(0, 3, GradeBand.Descent)],
        [new([0.5, 1.5, 2.5], [1.2, 2.6, 2.8])], [], [new([0.5, 1.5, 2.5], [6, 7, 8])],
        new([0, 0, 0, 1], false, false, false, true, false, false, 0))
    {
        FrameEdges = [0.9, 1.4, 2.2, 3.5],
        ForkEdges = [1.6, 2.5, 3.8, 5.8]
    };

    [Fact]
    public void RoughnessShadesEachRunAndGuidesTheLevelEdgesOfTheDrawnSensor()
    {
        var plot = Load(TerrainSignalKind.Roughness, Terrain);
        var scale = SufniThemes.SignalSeries.Terrain.Roughness;
        Assert.Equal([(0d, 1d, scale[(int)RoughnessClass.Calm]), (1d, 3d, scale[(int)RoughnessClass.Rough])], Shading(plot));
        Assert.Equal([0.9, 1.4, 2.2, 3.5], plot.PlottableList.OfType<FixedHorizontalLine>().Select(l => l.Y));
    }

    [Fact]
    public void GradeGuidesTheBandEdgesAndMarksZeroWithTheDirections()
    {
        var plot = Load(TerrainSignalKind.Grade, Terrain);
        Assert.Equal([.. TerrainProfileCalculator.GradeBandEdges, 0], plot.PlottableList.OfType<FixedHorizontalLine>().Select(l => l.Y));
        Assert.Equal(["▲ descent", "▼ climb"], plot.PlottableList.OfType<Annotation>().Select(a => a.Text));
    }

    [Fact]
    public void CurveWithoutValuesHasNoShadingOrGuides()
    {
        var plot = Load(TerrainSignalKind.Roughness, Terrain with { Frame = [] });
        Assert.Empty(plot.PlottableList.OfType<FixedHorizontalLine>());
    }

    [Fact]
    public void RearAxleCurveAloneShadesTheRuns()
    {
        var plot = Load(TerrainSignalKind.Roughness, Terrain with { Frame = [], RearAxle = [new([0.5, 1.5, 2.5], [1.9, 2.1, 2.2])] });
        Assert.Equal([(0d, 1d), (1d, 3d)], Shading(plot).Select(s => (s.Start, s.End)));
    }

    private static IEnumerable<(double Start, double End, Avalonia.Media.Color Colour)> Shading(Plot plot)
    {
        var scale = SufniThemes.SignalSeries.Terrain.Roughness;
        return plot.PlottableList.OfType<HorizontalSpan>()
            .Select(s => (s.X1, s.X2, Avalonia.Media.Color.FromRgb(s.FillStyle.Color.R, s.FillStyle.Color.G, s.FillStyle.Color.B)))
            .Where(s => scale.Contains(s.Item3));
    }

    private static Plot Load(TerrainSignalKind kind, TerrainPresentation terrain)
    {
        var plot = new Plot();
        new TerrainSignalPlot(plot).Load(terrain, TestTelemetryData.CreateProcessed(sampleRate: 20), kind);
        return plot;
    }
}
