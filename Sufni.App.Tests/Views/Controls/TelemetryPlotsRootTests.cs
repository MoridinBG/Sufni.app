using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

        Assert.Equal(247, travel.AllocatedGroupHeight);
        Assert.Equal(247, imu.AllocatedGroupHeight);
        Assert.Equal(215, travel.AllocatedPlotHeight);
        Assert.Equal(215, imu.AllocatedPlotHeight);
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
        var contentHeight = travel.AllocatedGroupHeight + imu.AllocatedGroupHeight + TelemetryPlotRowsPanel.BaseRowDividerHeight;
        Assert.Equal(430, contentHeight);
        Assert.True(contentHeight > 300);
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
        Assert.Equal(194.5, travel.AllocatedPlotHeight);
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
    public async Task TelemetryPlotsRoot_CollapsedBaseRows_DoNotShowResizeDividers()
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
        Assert.All(dividers, divider => Assert.False(divider.IsVisible));
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
        divider.ResetTargetRowToPreferredHeight();
        Measure(root, 400, 500);

        Assert.Equal(travel.GetPreferredGroupHeight(), travel.ManualGroupHeight);
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
