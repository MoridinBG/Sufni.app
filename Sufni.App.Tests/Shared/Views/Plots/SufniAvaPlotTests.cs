using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using Sufni.App.Shared.Views.Plots;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.Views.Plots;

[Collection("Ui")]
public class SufniAvaPlotTests
{

    [AvaloniaFact]
    public async Task ToScottPlotPixel_ScalesAvaloniaPoint_ToLastRenderPixelSpace()
    {
        var plot = CreatePlot();
        var host = new Window
        {
            Width = 400,
            Height = 220,
            Content = plot,
        };

        ViewTestHelpers.ShowWindow(host);
        plot.Measure(new Size(400, 220));
        plot.Arrange(new Rect(0, 0, 400, 220));
        await RenderAsync(plot, 800, 440);

        var pixel = plot.ToScottPlotPixel(new Point(100, 50));
        var size = plot.GetScottPlotPixelSize();

        Assert.Equal(200, pixel.X, precision: 6);
        Assert.Equal(100, pixel.Y, precision: 6);
        Assert.Equal(800, size.Width, precision: 6);
        Assert.Equal(440, size.Height, precision: 6);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static SufniAvaPlot CreatePlot()
    {
        var plot = new SufniAvaPlot
        {
            Width = 400,
            Height = 220,
        };

        plot.Plot.Axes.SetLimits(0, 10, 0, 10);
        return plot;
    }

    private static async Task RenderAsync(SufniAvaPlot plot, int width, int height)
    {
        plot.Plot.RenderInMemory(width, height);
        plot.Refresh();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

}
