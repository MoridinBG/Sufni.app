using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ScottPlot;
using ScottPlot.Interactivity.UserActionResponses;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Plots;
using static Sufni.App.Tests.Infrastructure.PlotTestHelpers;

namespace Sufni.App.Tests.Views.Plots;

public class SufniAvaPlotTests
{
    [AvaloniaFact]
    public async Task SufniAvaPlot_ShiftWheel_LeavesEventUnhandledWithoutZoomingPlot()
    {
        var plot = CreatePlot();
        var host = new Window
        {
            Width = 400,
            Height = 220,
            Content = plot,
        };

        host.Show();
        await RenderAsync(plot);

        var initialLimits = plot.Plot.Axes.GetLimits();

        var args = plot.InvokeWheel(GetDataAreaCenter(plot), KeyModifiers.Shift, new Vector(0, 1));
        await RenderAsync(plot);

        Assert.False(args.Handled);
        AssertAxisLimitsEqual(initialLimits, plot.Plot.Axes.GetLimits());

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public void PlotWheelZoomModifier_RunWithPrecisionZoom_ScalesAndRestoresZoomFraction()
    {
        var plot = CreatePlot();
        var wheelZoom = GetMouseWheelZoom(plot);
        var originalZoomFraction = wheelZoom.ZoomFraction;
        var observedZoomFraction = double.NaN;

        PlotWheelZoomModifier.RunWithPrecisionZoom([wheelZoom], () => observedZoomFraction = wheelZoom.ZoomFraction);

        Assert.Equal(originalZoomFraction / SufniAvaPlot.PrecisionZoomSlowdownFactor, observedZoomFraction);
        Assert.Equal(originalZoomFraction, wheelZoom.ZoomFraction);
    }

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

        host.Show();
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

    private static TestSufniAvaPlot CreatePlot()
    {
        var plot = new TestSufniAvaPlot
        {
            Width = 400,
            Height = 220,
        };

        plot.Plot.Axes.SetLimits(0, 10, 0, 10);
        return plot;
    }

    private static Task RenderAsync(SufniAvaPlot plot)
    {
        return RenderAsync(plot, 400, 220);
    }

    private static async Task RenderAsync(SufniAvaPlot plot, int width, int height)
    {
        plot.Plot.RenderInMemory(width, height);
        plot.Refresh();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static Point GetDataAreaCenter(SufniAvaPlot plot)
    {
        var dataRect = plot.Plot.LastRender.DataRect;
        var width = Math.Abs(dataRect.Right - dataRect.Left);
        var height = Math.Abs(dataRect.Bottom - dataRect.Top);

        Assert.True(width > 0);
        Assert.True(height > 0);

        return new Point(
            (dataRect.Left + dataRect.Right) / 2,
            (dataRect.Top + dataRect.Bottom) / 2);
    }

    private static MouseWheelZoom GetMouseWheelZoom(SufniAvaPlot plot) =>
        Assert.Single(plot.UserInputProcessor.UserActionResponses.OfType<MouseWheelZoom>());

    private static RawInputModifiers ToRawInputModifiers(KeyModifiers keyModifiers)
    {
        var rawModifiers = RawInputModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Alt))
        {
            rawModifiers |= RawInputModifiers.Alt;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Control))
        {
            rawModifiers |= RawInputModifiers.Control;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Meta))
        {
            rawModifiers |= RawInputModifiers.Meta;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Shift))
        {
            rawModifiers |= RawInputModifiers.Shift;
        }

        return rawModifiers;
    }

    private sealed class TestSufniAvaPlot : SufniAvaPlot
    {
        public PointerWheelEventArgs InvokeWheel(Point point, KeyModifiers keyModifiers, Vector delta)
        {
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var args = new PointerWheelEventArgs(
                this,
                pointer,
                this,
                point,
                timestamp: 0,
                new PointerPointProperties(ToRawInputModifiers(keyModifiers), PointerUpdateKind.Other),
                keyModifiers,
                delta);

            OnPointerWheelChanged(args);
            return args;
        }
    }
}
