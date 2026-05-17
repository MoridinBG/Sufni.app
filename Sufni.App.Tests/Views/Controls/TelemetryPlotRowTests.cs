using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class TelemetryPlotRowTests
{
    [AvaloniaFact]
    public async Task TelemetryPlotRow_CollapsesAndExpands_FromHeaderClick()
    {
        var row = CreateRow("Travel");
        await using var mounted = await MountAsync(row);

        Measure(row, 400, row.GetPreferredGroupHeight());
        Assert.True(row.IsExpanded);
        Assert.Equal(212, row.AllocatedGroupHeight);

        var header = Assert.Single(row.GetVisualDescendants().OfType<Button>());
        header.Command?.Execute(null);
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewTestHelpers.FlushDispatcherAsync();
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.False(row.IsExpanded);
        Assert.Equal(row.CollapsedHeaderHeight, row.AllocatedGroupHeight);

        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewTestHelpers.FlushDispatcherAsync();
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.True(row.IsExpanded);
        Assert.Equal(212, row.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_HiddenPresentation_RemovesRowFromLayout()
    {
        var row = CreateRow("Velocity");
        row.PresentationState = SurfacePresentationState.Hidden;

        await using var mounted = await MountAsync(row);
        Measure(row, 400, 260);

        Assert.False(row.IsVisible);
        Assert.Equal(0, row.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_NestedPlot_UsesHostPlotHeight()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);

        await using var mounted = await MountAsync(travel);
        Measure(travel, 400, travel.GetPreferredGroupHeight());

        Assert.Equal(travel.AllocatedPlotHeight, velocity.AllocatedPlotHeight);
        Assert.Equal(180, travel.AllocatedPlotHeight);
        Assert.Equal(424, travel.AllocatedGroupHeight);
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

    private static async Task<MountedRow> MountAsync(TelemetryPlotRow row)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = await ViewTestHelpers.ShowViewAsync(row);
        return new MountedRow(host);
    }

    private static void Measure(TelemetryPlotRow row, double width, double height)
    {
        row.ApplyAllocatedGroupHeight(height);
        row.Measure(new Size(width, row.AllocatedGroupHeight));
        row.Arrange(new Rect(0, 0, width, row.AllocatedGroupHeight));
    }

    private sealed class MountedRow(Window host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
