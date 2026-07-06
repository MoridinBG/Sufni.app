using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Sufni.App.Theming;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;

using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.Views.Controls;

[Collection("Ui")]
public class SignalRowsRootTests
{
    [AvaloniaFact]
    public async Task SignalRowsRoot_FiniteViewportAbovePreferred_GrowsBaseRowsEqually()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 500);

        Assert.Equal(250, travel.AllocatedGroupHeight);
        Assert.Equal(250, imu.AllocatedGroupHeight);
        Assert.Equal(218, travel.AllocatedPlotHeight);
        Assert.Equal(218, imu.AllocatedPlotHeight);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_FiniteViewportBelowPreferred_KeepsPreferredRowsForScrollExtent()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 300);

        Assert.Equal(212, travel.AllocatedGroupHeight);
        Assert.Equal(212, imu.AllocatedGroupHeight);
        var contentHeight = travel.AllocatedGroupHeight + imu.AllocatedGroupHeight;
        Assert.Equal(424, contentHeight);
        Assert.True(contentHeight > 300);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_CtrlWheelOverPlot_ScrollsRowsWithoutZoomingPlot()
    {
        EnsureFluentTheme();
        var plot = CreateWheelPlot();
        var root = CreateScrollableRootWithWheelPlot(plot);

        await using var mounted = await MountAsync(root, width: 400, height: 300);
        await LayoutForWheelAsync(root, plot);

        var scrollViewer = FindRowsScrollViewer(root);
        var initialOffset = scrollViewer.Offset.Y;
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
    public async Task SignalRowsRoot_PlainWheelOverPlot_DoesNotScrollRows()
    {
        EnsureFluentTheme();
        var plot = CreateWheelPlot();
        var root = CreateScrollableRootWithWheelPlot(plot);

        await using var mounted = await MountAsync(root, width: 400, height: 300);
        await LayoutForWheelAsync(root, plot);

        var scrollViewer = FindRowsScrollViewer(root);
        var initialOffset = scrollViewer.Offset.Y;

        var args = plot.InvokeWheel(mounted.Host, GetDataAreaCenterPoint(plot), KeyModifiers.None, new Vector(0, -1));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(initialOffset, scrollViewer.Offset.Y);
        Assert.True(args.Handled);
        Assert.True(plot.IsPointInDataArea(args.GetPosition(plot)));
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_CtrlWheelUsesHorizontalDeltaFallback_ForVerticalScroll()
    {
        EnsureFluentTheme();
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
        EnsureFluentTheme();
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

    [AvaloniaFact]
    public async Task SignalRowsRoot_ViewportResize_RecomputesRowHeights()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 500);
        Assert.Equal(250, travel.AllocatedGroupHeight);
        Assert.Equal(250, imu.AllocatedGroupHeight);

        Measure(root, 400, 600);
        Assert.Equal(300, travel.AllocatedGroupHeight);
        Assert.Equal(300, imu.AllocatedGroupHeight);

        Measure(root, 400, 360);
        Assert.Equal(212, travel.AllocatedGroupHeight);
        Assert.Equal(212, imu.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_NestedRowPlotHeight_MatchesHostPlotHeight()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 700);

        Assert.Equal(travel.AllocatedPlotHeight, velocity.AllocatedPlotHeight);
        Assert.Equal(196, travel.AllocatedPlotHeight);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_HiddenBaseRows_DoNotContributeHeight()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        imu.PresentationState = SurfacePresentationState.Hidden;
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 300);

        Assert.Equal(300, travel.AllocatedGroupHeight);
        Assert.Equal(0, imu.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_CollapsedBaseRows_KeepCompactOverlayDividers()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        var gps = CreateRow("GPS");
        travel.IsExpanded = false;
        imu.IsExpanded = false;
        gps.IsExpanded = false;
        var root = CreateRoot(travel, imu, gps);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 200);

        var dividers = root.GetVisualDescendants().OfType<SignalBaseRowDivider>().ToArray();
        Assert.Equal(2, dividers.Length);
        Assert.All(dividers, divider => Assert.True(divider.IsVisible));
        Assert.All(dividers, divider => Assert.False(divider.IsHitTestVisible));
        Assert.Equal(32, travel.AllocatedGroupHeight);
        Assert.Equal(32, imu.AllocatedGroupHeight);
        Assert.Equal(32, gps.AllocatedGroupHeight);
        Assert.Equal(0, travel.Bounds.Y);
        Assert.Equal(32, imu.Bounds.Y);
        Assert.Equal(64, gps.Bounds.Y);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_DividerDoubleClick_ResetsTargetRowToPreferredHeight()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 500);
        travel.ManualGroupHeight = 300;

        var divider = Assert.Single(root.GetVisualDescendants().OfType<SignalBaseRowDivider>());
        Assert.True(divider.IsVisible);
        Assert.True(divider.IsHitTestVisible);
        divider.ResetTargetRowToPreferredHeight();
        Measure(root, 400, 500);

        Assert.Equal(travel.GetPreferredGroupHeight(), travel.ManualGroupHeight);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_DropHostedRowBetweenRootRows_MakesItRoot()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 500);

        var dropped = root.TryDropDraggedRowAtPoint(velocity, new Point(10, travel.Bounds.Bottom - 1));
        Measure(root, 400, 500);

        Assert.True(dropped);
        Assert.Same(travel, root.Rows[0]);
        Assert.Same(velocity, root.Rows[1]);
        Assert.Same(imu, root.Rows[2]);
        Assert.Empty(travel.ChildRows);
        Assert.Equal(0, velocity.TitleLeftInset);
        var expectedRootTheme = SufniThemes.FromVariant(velocity.ActualThemeVariant).SignalRow.Root;
        AssertBrushColor(expectedRootTheme.Container, velocity.RowBackground);
        AssertBrushColor(expectedRootTheme.Header, velocity.HeaderBackground);
        Assert.Equal(expectedRootTheme.PlotFigure, velocity.PlotFigureBackground);
        Assert.Equal(expectedRootTheme.PlotData, velocity.PlotDataBackground);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_DropHostedRowOnHostedHeader_NestsItUnderHostedRow()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);
        var elevation = CreateRow("Elevation");
        var gps = CreateRow("GPS");
        gps.ChildRows.Add(elevation);
        var root = CreateRoot(travel, gps);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 700);

        var rowsPanel = Assert.Single(root.GetVisualDescendants().OfType<Panel>(), panel => panel.Name == "RowsPanel");
        var velocityOrigin = velocity.TranslatePoint(new Point(0, 0), rowsPanel);
        Assert.NotNull(velocityOrigin);

        var dropped = root.TryDropDraggedRowAtPoint(elevation, velocityOrigin.Value + new Point(10, 16));
        Measure(root, 400, 700);

        Assert.True(dropped);
        Assert.Empty(gps.ChildRows);
        Assert.Same(elevation, Assert.Single(velocity.ChildRows));
        Assert.Equal(32, elevation.TitleLeftInset);
        Assert.NotNull(elevation.RowBackground);
    }

    [AvaloniaFact]
    public void SignalRowsRoot_MoveRowIntoDescendant_IsIgnored()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);
        var root = CreateRoot(travel);

        var moved = root.MoveRowInto(travel, velocity);

        Assert.False(moved);
        Assert.Same(travel, Assert.Single(root.Rows));
        Assert.Same(velocity, Assert.Single(travel.ChildRows));
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_DragFeedback_HighlightsHeaderDropTarget()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);
        var elevation = CreateRow("Elevation");
        var gps = CreateRow("GPS");
        gps.ChildRows.Add(elevation);
        var root = CreateRoot(travel, gps);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 700);

        var rowsPanel = Assert.Single(root.GetVisualDescendants().OfType<Panel>(), panel => panel.Name == "RowsPanel");
        var velocityOrigin = velocity.TranslatePoint(new Point(0, 0), rowsPanel);
        Assert.NotNull(velocityOrigin);

        root.BeginRowDragFeedback(elevation);
        root.UpdateRowDragFeedbackAtPoint(elevation, velocityOrigin.Value + new Point(10, 16));

        Assert.True(elevation.IsDragFeedbackVisible);
        Assert.True(velocity.IsDropTargetFeedbackVisible);
        Assert.False(root.IsRootDropIndicatorVisible);

        root.EndRowDragFeedback(elevation);

        Assert.False(elevation.IsDragFeedbackVisible);
        Assert.False(velocity.IsDropTargetFeedbackVisible);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_DragFeedback_ShowsRootInsertionLine()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 500);

        root.BeginRowDragFeedback(velocity);
        root.UpdateRowDragFeedbackAtPoint(velocity, new Point(10, travel.Bounds.Bottom - 1));

        Assert.True(velocity.IsDragFeedbackVisible);
        Assert.True(root.IsRootDropIndicatorVisible);
        Assert.False(travel.IsDropTargetFeedbackVisible);
        Assert.True(root.RootDropIndicatorY >= 0);

        root.EndRowDragFeedback(velocity);

        Assert.False(velocity.IsDragFeedbackVisible);
        Assert.False(root.IsRootDropIndicatorVisible);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_SignalLayoutPreferences_AppliesStoredHierarchyAndExpansion()
    {
        var travel = CreateRow("Travel", SignalRowIds.Travel);
        var velocity = CreateRow("Velocity", SignalRowIds.Velocity);
        var imu = CreateRow("IMU", SignalRowIds.Imu);
        var speed = CreateRow("Speed", SignalRowIds.Speed);
        var elevation = CreateRow("Elevation", SignalRowIds.Elevation);
        travel.ChildRows.Add(velocity);
        speed.ChildRows.Add(elevation);
        var root = CreateRoot(travel, imu, speed);
        root.SignalLayoutPreferences = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(
                SignalRowIds.Imu,
                isExpanded: false,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Velocity),
                ]),
            new SignalLayoutRowPreferences(SignalRowIds.Travel),
        ]);

        await using var mounted = await MountAsync(root);

        Assert.Equal(["IMU", "Travel", "Speed"], root.Rows.Select(row => row.Title!).ToArray());
        Assert.False(root.Rows[0].IsExpanded);
        Assert.Equal(["Velocity"], root.Rows[0].ChildRows.Select(row => row.Title!).ToArray());
        Assert.Empty(root.Rows[1].ChildRows);
        Assert.Equal(["Elevation"], root.Rows[2].ChildRows.Select(row => row.Title!).ToArray());
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_SignalLayoutPreferences_AppliesStoredHeightRatios()
    {
        var travel = CreateRow("Travel", SignalRowIds.Travel);
        var imu = CreateRow("IMU", SignalRowIds.Imu);
        var root = CreateRoot(travel, imu);
        root.SignalLayoutPreferences = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Travel, heightRatio: 0.25),
            new SignalLayoutRowPreferences(SignalRowIds.Imu, heightRatio: 0.75),
        ]);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 800);

        Assert.Equal(200, travel.AllocatedGroupHeight);
        Assert.Equal(600, imu.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_SignalLayoutPreferences_UsesDefaults_WhenAnyVisibleRootRowHasNoHeightRatio()
    {
        var travel = CreateRow("Travel", SignalRowIds.Travel);
        var imu = CreateRow("IMU", SignalRowIds.Imu);
        var root = CreateRoot(travel, imu);
        root.SignalLayoutPreferences = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Travel, heightRatio: 0.25),
            new SignalLayoutRowPreferences(SignalRowIds.Imu),
        ]);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 800);

        Assert.Equal(400, travel.AllocatedGroupHeight);
        Assert.Equal(400, imu.AllocatedGroupHeight);
        Assert.Null(travel.ManualGroupHeightRatio);
        Assert.Null(imu.ManualGroupHeightRatio);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_CommitManualRowSizePreferences_PublishesHeightRatios()
    {
        var travel = CreateRow("Travel", SignalRowIds.Travel);
        var imu = CreateRow("IMU", SignalRowIds.Imu);
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 800);

        travel.ManualGroupHeight = 300;
        imu.ManualGroupHeight = 500;
        root.CommitManualRowSizePreferences();

        Assert.Equal(0.375, root.SignalLayoutPreferences.Rows[0].HeightRatio);
        Assert.Equal(0.625, root.SignalLayoutPreferences.Rows[1].HeightRatio);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_SignalLayoutPreferences_UpdatesWhenRowsMoveOrCollapse()
    {
        var travel = CreateRow("Travel", SignalRowIds.Travel);
        var velocity = CreateRow("Velocity", SignalRowIds.Velocity);
        var imu = CreateRow("IMU", SignalRowIds.Imu);
        travel.ChildRows.Add(velocity);
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);

        travel.IsExpanded = false;
        Assert.True(root.MoveRowToRoot(velocity, 1));

        var stored = root.SignalLayoutPreferences;
        Assert.Equal(
            [SignalRowIds.Travel, SignalRowIds.Velocity, SignalRowIds.Imu],
            stored.Rows.Select(row => row.RowId).ToArray());
        Assert.False(stored.Rows[0].IsExpanded);
        Assert.Empty(stored.Rows[0].Children);
    }

    [AvaloniaFact]
    public async Task SignalRowsRoot_SignalLayoutPreferences_ReparentsRootRowUnderAnotherRow()
    {
        var travel = CreateRow("Travel", SignalRowIds.Travel);
        var imu = CreateRow("IMU", SignalRowIds.Imu);
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);

        root.SignalLayoutPreferences = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(
                SignalRowIds.Travel,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Imu),
                ]),
        ]);

        Assert.Same(travel, Assert.Single(root.Rows));
        Assert.Same(imu, Assert.Single(travel.ChildRows));
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

    private static async Task<MountedRoot> MountAsync(SignalRowsRoot root)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = await ViewTestHelpers.ShowViewAsync(root);
        return new MountedRoot(host);
    }

    private static async Task<MountedRoot> MountAsync(SignalRowsRoot root, double width, double height)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = new Window
        {
            Width = width,
            Height = height,
            Content = root,
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        await ViewTestHelpers.FlushDispatcherAsync();

        return new MountedRoot(host);
    }

    private static void Measure(SignalRowsRoot root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    private static void EnsureFluentTheme()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Add(new FluentTheme());
        }
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

    private static void AssertBrushColor(Color expected, IBrush? brush)
    {
        var solidBrush = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(expected, solidBrush.Color);
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

    private sealed class MountedRoot(Window host) : IAsyncDisposable
    {
        public Window Host => host;

        public async ValueTask DisposeAsync()
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
