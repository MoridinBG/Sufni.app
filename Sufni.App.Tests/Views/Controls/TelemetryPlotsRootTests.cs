using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class TelemetryPlotsRootTests
{
    [AvaloniaFact]
    public async Task TelemetryPlotsRoot_FiniteViewportAbovePreferred_GrowsBaseRowsEqually()
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
    public async Task TelemetryPlotsRoot_FiniteViewportBelowPreferred_KeepsPreferredRowsForScrollExtent()
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
    public async Task TelemetryPlotsRoot_ViewportResize_RecomputesRowHeights()
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
    public async Task TelemetryPlotsRoot_NestedRowPlotHeight_MatchesHostPlotHeight()
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
    public async Task TelemetryPlotsRoot_HiddenBaseRows_DoNotContributeHeight()
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
    public async Task TelemetryPlotsRoot_CollapsedBaseRows_KeepCompactOverlayDividers()
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

        var dividers = root.GetVisualDescendants().OfType<TelemetryBaseRowDivider>().ToArray();
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
    public async Task TelemetryPlotsRoot_DividerDoubleClick_ResetsTargetRowToPreferredHeight()
    {
        var travel = CreateRow("Travel");
        var imu = CreateRow("IMU");
        var root = CreateRoot(travel, imu);

        await using var mounted = await MountAsync(root);
        Measure(root, 400, 500);
        travel.ManualGroupHeight = 300;

        var divider = Assert.Single(root.GetVisualDescendants().OfType<TelemetryBaseRowDivider>());
        Assert.True(divider.IsVisible);
        Assert.True(divider.IsHitTestVisible);
        divider.ResetTargetRowToPreferredHeight();
        Measure(root, 400, 500);

        Assert.Equal(travel.GetPreferredGroupHeight(), travel.ManualGroupHeight);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotsRoot_DropHostedRowBetweenRootRows_MakesItRoot()
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
        Assert.Null(velocity.RowBackground);
        Assert.Null(velocity.HeaderBackground);
        Assert.Equal(Color.Parse("#15191C"), velocity.PlotFigureBackground);
        Assert.Equal(Color.Parse("#20262B"), velocity.PlotDataBackground);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotsRoot_DropHostedRowOnHostedHeader_NestsItUnderHostedRow()
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
        Assert.Equal(12, elevation.TitleLeftInset);
        Assert.NotNull(elevation.RowBackground);
    }

    [AvaloniaFact]
    public void TelemetryPlotsRoot_MoveRowIntoDescendant_IsIgnored()
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
    public async Task TelemetryPlotsRoot_DragFeedback_HighlightsHeaderDropTarget()
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
    public async Task TelemetryPlotsRoot_DragFeedback_ShowsRootInsertionLine()
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

    private static TelemetryPlotsRoot CreateRoot(params TelemetryPlotRow[] rows)
    {
        var root = new TelemetryPlotsRoot();
        foreach (var row in rows)
        {
            root.Rows.Add(row);
        }

        return root;
    }

    private static TelemetryPlotRow CreateRow(string title)
    {
        return new TelemetryPlotRow
        {
            Title = title,
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

    private static async Task<MountedRoot> MountAsync(TelemetryPlotsRoot root)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = await ViewTestHelpers.ShowViewAsync(root);
        return new MountedRoot(host);
    }

    private static void Measure(TelemetryPlotsRoot root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    private sealed class MountedRoot(Window host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
