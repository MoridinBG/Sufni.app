using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Sessions.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Shared.Common;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;

using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Signals.Views.Controls;

[Collection("Ui")]
public class SignalRowsRootTests
{
    [AvaloniaFact]
    public async Task SignalRowsRoot_CtrlWheelOverPlot_ScrollsRowsWithoutZoomingPlot()
    {
        ViewTestHelpers.EnsureFluentTheme();
        var plot = CreateWheelPlot();
        var root = CreateScrollableRootWithWheelPlot(plot);

        await using var mounted = await MountAsync(root, width: 400, height: 300);
        await LayoutForWheelAsync(root, plot);

        var scrollViewer = FindRowsScrollViewer(root);
        var initialOffset = scrollViewer.Offset.Y;
        plot.InvokeWheel(mounted.Host, GetDataAreaCenterPoint(plot), KeyModifiers.None, new Vector(0, -1));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(initialOffset, scrollViewer.Offset.Y);
        var initialLimits = plot.Plot.Axes.GetLimits();

        var args = plot.InvokeWheel(mounted.Host, GetDataAreaCenterPoint(plot), KeyModifiers.Control, new Vector(0, -1));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(args.Handled);
        Assert.True(
            scrollViewer.Offset.Y > initialOffset,
            $"Offset did not increase. Initial={initialOffset}, Actual={scrollViewer.Offset.Y}, Extent={scrollViewer.Extent}, Viewport={scrollViewer.Viewport}, ScrollBarMaximum={scrollViewer.ScrollBarMaximum}.");
        AssertAxisLimitsEqual(initialLimits, plot.Plot.Axes.GetLimits());
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_CtrlWheelUsesHorizontalDeltaFallback_ForVerticalScroll()
    {
        ViewTestHelpers.EnsureFluentTheme();
        var plot = CreateWheelPlot();
        var root = CreateScrollableRootWithWheelPlot(plot);

        await using var mounted = await MountAsync(root, width: 400, height: 300);
        await LayoutForWheelAsync(root, plot);

        var scrollViewer = FindRowsScrollViewer(root);
        var initialOffset = scrollViewer.Offset.Y;

        var args = plot.InvokeWheel(mounted.Host, GetDataAreaCenterPoint(plot), KeyModifiers.Control, new Vector(-1, 0));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(args.Handled);
        Assert.True(
            scrollViewer.Offset.Y > initialOffset,
            $"Offset did not increase. Initial={initialOffset}, Actual={scrollViewer.Offset.Y}, Extent={scrollViewer.Extent}, Viewport={scrollViewer.Viewport}, ScrollBarMaximum={scrollViewer.ScrollBarMaximum}.");
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_CtrlWheelAtScrollBoundary_DoesNotZoomPlot()
    {
        ViewTestHelpers.EnsureFluentTheme();
        var plot = CreateWheelPlot();
        var root = CreateScrollableRootWithWheelPlotAtEnd(plot);

        await using var mounted = await MountAsync(root, width: 400, height: 300);
        await LayoutForWheelAsync(root, plot);

        var scrollViewer = FindRowsScrollViewer(root);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.ScrollBarMaximum.Y);
        await ViewTestHelpers.FlushDispatcherAsync();
        var initialOffset = scrollViewer.Offset.Y;
        var initialLimits = plot.Plot.Axes.GetLimits();

        var args = plot.InvokeWheel(mounted.Host, GetDataAreaCenterPoint(plot), KeyModifiers.Control, new Vector(0, -1));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(args.Handled);
        Assert.Equal(initialOffset, scrollViewer.Offset.Y);
        AssertAxisLimitsEqual(initialLimits, plot.Plot.Axes.GetLimits());
    }

    private static SignalRowsRoot CreateRoot(params SignalRow[] rows)
    {
        var root = new SignalRowsRoot();
        foreach (var row in rows)
        {
            root.Rows.Add(row);
        }

        return root;
    }

    private static SignalRow CreateRow(string title)
        => CreateRow(title, rowId: null);

    private static SignalRow CreateRowWithPlot(string title, Control plotContent)
    {
        var row = CreateRow(title);
        row.PlotContent = plotContent;
        return row;
    }

    private static SignalRow CreateRow(string title, string? rowId)
    {
        return new SignalRow
        {
            Title = title,
            RowId = rowId,
            PresentationState = SurfacePresentationState.Ready,
            PlotContent = new Border(),
            PlaceholderContent = new Border(),
            PreferredPlotHeight = 180,
            MinimumPlotHeight = 96,
            HeaderHeight = 32,
            CollapsedHeaderHeight = 32,
            ChildRowGap = 4,
        };
    }

    private static TestWheelPlot CreateWheelPlot()
    {
        var plot = new TestWheelPlot
        {
            Width = 400,
            Height = 180,
        };

        plot.Plot.Axes.SetLimits(0, 10, 0, 10);
        plot.Plot.Add.Scatter(new double[] { 0, 10 }, new double[] { 0, 10 });
        return plot;
    }

    private static SignalRowsRoot CreateScrollableRootWithWheelPlot(TestWheelPlot plot)
    {
        return CreateRoot(
            CreateRowWithPlot("Travel", plot),
            CreateRow("Velocity"),
            CreateRow("IMU"));
    }

    private static SignalRowsRoot CreateScrollableRootWithWheelPlotAtEnd(TestWheelPlot plot)
    {
        return CreateRoot(
            CreateRow("Travel"),
            CreateRow("Velocity"),
            CreateRowWithPlot("IMU", plot));
    }

    private static async Task LayoutForWheelAsync(SignalRowsRoot root, TestWheelPlot plot)
    {
        Measure(root, 400, 300);
        root.UpdateLayout();
        RenderPlotInMemory(plot);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static ScrollViewer FindRowsScrollViewer(SignalRowsRoot root)
    {
        return Assert.Single(
            root.GetVisualDescendants().OfType<ScrollViewer>(),
            scrollViewer => scrollViewer.Name == "RowsScrollViewer");
    }

    private static async Task<MountedView<SignalRowsRoot>> MountAsync(SignalRowsRoot root, double width, double height)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = new Window
        {
            Width = width,
            Height = height,
            Content = root,
        };

        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        await ViewTestHelpers.FlushDispatcherAsync();

        return new MountedView<SignalRowsRoot>(host, root);
    }

    private static void Measure(SignalRowsRoot root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }



    private static RawInputModifiers ToRawInputModifiers(KeyModifiers keyModifiers)
    {
        var rawModifiers = RawInputModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Control))
        {
            rawModifiers |= RawInputModifiers.Control;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Shift))
        {
            rawModifiers |= RawInputModifiers.Shift;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Alt))
        {
            rawModifiers |= RawInputModifiers.Alt;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Meta))
        {
            rawModifiers |= RawInputModifiers.Meta;
        }

        return rawModifiers;
    }

    private sealed class TestWheelPlot : SufniAvaPlot
    {
        public PointerWheelEventArgs InvokeWheel(TopLevel host, Point point, KeyModifiers keyModifiers, Vector delta)
        {
            PointerWheelEventArgs? received = null;
            var rowsScrollViewer = this
                .GetVisualAncestors()
                .OfType<ScrollViewer>()
                .Single(scrollViewer => scrollViewer.Name == "RowsScrollViewer");
            rowsScrollViewer.AddHandler(
                InputElement.PointerWheelChangedEvent,
                (_, args) => received ??= args,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            var hostPoint = this.TranslatePoint(point, host);
            Assert.NotNull(hostPoint);

            var rawModifiers = ToRawInputModifiers(keyModifiers);
            host.MouseMove(hostPoint.Value, rawModifiers);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            host.MouseWheel(hostPoint.Value, delta, rawModifiers);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return received ?? throw new InvalidOperationException("The rows scroll viewer did not receive a wheel event.");
        }
    }

}
