using System.Linq;
using ScottPlot;
using SkiaSharp;
using Sufni.App.LiveDaq.Plots;

namespace Sufni.App.Tests.LiveDaq.Plots;

public class ReusableScrollLeftViewTests
{
    [Fact]
    public void GetSegments_MatchesScottPlotScrollLeft_AndReusesFullBuffer()
    {
        const int capacity = 8;
        var plot = new Plot();
        var streamer = plot.Add.DataStreamer(capacity);
        streamer.ViewScrollLeft();
        var control = streamer.Renderer;
        plot.Axes.SetLimits(0, capacity, -10, 10);

        streamer.AddRange([1, 2, 3, 4]);
        plot.RenderInMemory(320, 180);

        using var surface = SKSurface.Create(new SKImageInfo(320, 180));
        var renderPack = new RenderPack(plot, new PixelRect(0, 0, 320, 180), surface.Canvas);
        var sut = new ReusableScrollLeftView(streamer);

        AssertSegmentsEqual(control.GetSegments(renderPack), sut.GetSegments(renderPack));

        streamer.AddRange([5, 6, 7, 8, 9, 10]);
        AssertSegmentsEqual(control.GetSegments(renderPack), sut.GetSegments(renderPack));
        var fullBuffer = Assert.Single(sut.GetSegments(renderPack));

        streamer.AddRange([11, 12]);
        AssertSegmentsEqual(control.GetSegments(renderPack), sut.GetSegments(renderPack));
        Assert.Same(fullBuffer, Assert.Single(sut.GetSegments(renderPack)));
    }

    [Fact]
    public void Render_MatchesScottPlotScrollLeft_AfterRepeatedScrolling()
    {
        var (controlPlot, controlStreamer) = CreatePlot(useReusableView: false);
        var (candidatePlot, candidateStreamer) = CreatePlot(useReusableView: true);
        var initial = Enumerable.Range(0, 8).Select(index => Math.Sin(index)).ToArray();
        controlStreamer.AddRange(initial);
        candidateStreamer.AddRange(initial);

        Assert.Equal(RenderPng(controlPlot), RenderPng(candidatePlot));

        for (var batch = 0; batch < 12; batch++)
        {
            var values = Enumerable.Range(0, 2)
                .Select(index => Math.Sin(8 + batch * 2 + index))
                .ToArray();
            controlStreamer.AddRange(values);
            candidateStreamer.AddRange(values);
            Assert.Equal(RenderPng(controlPlot), RenderPng(candidatePlot));
        }
    }

    private static (Plot Plot, ScottPlot.Plottables.DataStreamer Streamer) CreatePlot(
        bool useReusableView)
    {
        var plot = new Plot();
        var streamer = plot.Add.DataStreamer(8);
        if (useReusableView)
        {
            streamer.ViewCustom(new ReusableScrollLeftView(streamer));
        }
        else
        {
            streamer.ViewScrollLeft();
        }

        streamer.ManageAxisLimits = false;
        streamer.LineWidth = 2;
        plot.Axes.SetLimits(0, 8, -1.5, 1.5);
        return (plot, streamer);
    }

    private static byte[] RenderPng(Plot plot)
    {
        using var surface = SKSurface.Create(new SKImageInfo(320, 180));
        plot.Render(surface.Canvas, new PixelRect(0, 0, 320, 180));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void AssertSegmentsEqual(
        IReadOnlyList<Pixel[]> expected,
        IReadOnlyList<Pixel[]> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var segmentIndex = 0; segmentIndex < expected.Count; segmentIndex++)
        {
            Assert.Equal(expected[segmentIndex], actual[segmentIndex]);
        }
    }
}
