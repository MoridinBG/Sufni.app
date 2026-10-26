using ScottPlot;
using Sufni.App.Sessions.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Plots;

public class ViewportReducedScatterTests
{
    [Fact]
    public void FrozenViewportTrajectoryUsesBoundedEndpointPreservingLevels()
    {
        var frontStroke = CreateStroke(phase: 0);
        var rearStroke = CreateStroke(phase: Math.PI / 3);
        var side = new PositionVelocitySideTrace([frontStroke, rearStroke], new double[1, 1]);
        var scatter = new ViewportReducedScatter(side)
        {
            Color = Color.FromHex("#2f80ed").WithAlpha(0.10),
            LineWidth = 1,
        };
        scatter.MarkerStyle.IsVisible = false;

        using var plot = new Plot();
        plot.Add.Plottable(scatter);
        plot.Axes.SetLimits(0, 100, -1000, 1000);

        plot.RenderInMemory(900, 540);
        var fullViewCoordinateCount = scatter.RenderedCoordinateCount;
        Assert.InRange(scatter.CurrentScreenSpaceErrorPixels, 0, 0.5);
        Assert.InRange(fullViewCoordinateCount, 4, frontStroke.Count + rearStroke.Count - 1);

        plot.Axes.SetLimits(27.5, 72.5, -450, 450);
        plot.RenderInMemory(900, 540);
        Assert.InRange(scatter.CurrentScreenSpaceErrorPixels, 0, 0.5);
        Assert.True(scatter.RenderedCoordinateCount >= fullViewCoordinateCount);

        plot.Axes.SetLimits(13.5, 68.5, -350, 750);
        plot.RenderInMemory(900, 540);
        Assert.InRange(scatter.CurrentScreenSpaceErrorPixels, 0, 0.5);

        var data = scatter.Data.GetScatterPoints();
        Assert.Equal(frontStroke.Count + rearStroke.Count + 1, data.Count);
        Assert.Equal(frontStroke.PositionPercent[0], data[0].X);
        Assert.Equal(frontStroke.VelocityMmPerSecond[0], data[0].Y);
        Assert.Equal(frontStroke.PositionPercent[^1], data[frontStroke.Count - 1].X);
        Assert.Equal(frontStroke.VelocityMmPerSecond[^1], data[frontStroke.Count - 1].Y);
        Assert.True(double.IsNaN(data[frontStroke.Count].X));
        Assert.True(double.IsNaN(data[frontStroke.Count].Y));
        Assert.Equal(rearStroke.PositionPercent[0], data[frontStroke.Count + 1].X);
        Assert.Equal(rearStroke.VelocityMmPerSecond[0], data[frontStroke.Count + 1].Y);
        Assert.Equal(rearStroke.PositionPercent[^1], data[^1].X);
        Assert.Equal(rearStroke.VelocityMmPerSecond[^1], data[^1].Y);
    }

    [Fact]
    public void Dispose_ReleasesBuiltPaths_AndIsIdempotent()
    {
        var scatter = new ViewportReducedScatter(
            new PositionVelocitySideTrace([CreateStroke(phase: 0)], new double[1, 1]));
        using var plot = new Plot();
        plot.Add.Plottable(scatter);
        plot.Axes.SetLimits(0, 100, -1000, 1000);
        plot.RenderInMemory(900, 540);

        scatter.Dispose();
        scatter.Dispose();

        Assert.True(scatter.IsDisposed);
        plot.RenderInMemory(900, 540);
    }

    private static PositionVelocityStrokeTrace CreateStroke(double phase)
    {
        var positions = Enumerable.Range(0, 1001).Select(index => index / 10.0).ToArray();
        var velocities = positions
            .Select(position => 500 * Math.Sin(position / 8.0 + phase))
            .ToArray();
        return new PositionVelocityStrokeTrace(positions, velocities);
    }
}
