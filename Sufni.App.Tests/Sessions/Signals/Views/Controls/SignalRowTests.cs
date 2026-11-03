using Sufni.App.Sessions.Signals.Views.Controls;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Shared.Common;

using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Signals.Views.Controls;

[Collection("Ui")]
public class SignalRowTests
{
    [AvaloniaFact]
    public async Task SignalRow_CollapseDetachesPlotContent_AndExpandReattaches()
    {
        var plotContent = new Border();
        var row = CreateRow("Travel");
        row.PlotContent = plotContent;
        await using var mounted = await MountAsync(row);

        Assert.Contains(plotContent, row.GetVisualDescendants());

        var header = Assert.Single(row.GetVisualDescendants().OfType<Button>());
        await ClickHeaderAsync(mounted, header);

        Assert.DoesNotContain(plotContent, row.GetVisualDescendants());

        await ClickHeaderAsync(mounted, header);

        Assert.Contains(plotContent, row.GetVisualDescendants());
    }

    [AvaloniaFact]
    public async Task SignalRow_HeaderClick_ToleratesSmallPointerMovement()
    {
        var row = CreateRow("Travel");
        await using var mounted = await MountAsync(row);

        var header = Assert.Single(row.GetVisualDescendants().OfType<Button>());

        await ClickHeaderAsync(mounted, header, new Point(200, 16), new Point(204, 18));

        Assert.False(row.IsExpanded);
    }

    [AvaloniaFact]
    public async Task SignalRow_DoubleTapPlotSlot_RaisesPlotZoomRequested()
    {
        var plotContent = new Border();
        var row = CreateRow("Travel");
        row.PlotContent = plotContent;
        PlotZoomRequestedEventArgs? received = null;
        row.AddHandler(
            PlotZoomContainer.PlotZoomRequestedEvent,
            (_, args) => received = args);

        await using var mounted = await MountAsync(row);

        RaiseDoubleTapped(plotContent);

        Assert.NotNull(received);
        Assert.Same(
            Assert.Single(row.GetVisualDescendants().OfType<PlotZoomContainer>()),
            received.Container);
    }

    private static SignalRow CreateRow(string title)
    {
        return new SignalRow
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

    private static async Task<MountedView<SignalRow>> MountAsync(SignalRow row)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var root = new SignalRowsRoot();
        root.Rows.Add(row);
        var host = await ViewTestHelpers.ShowViewAsync(root);
        return new MountedView<SignalRow>(host, row);
    }

    private static async Task ClickHeaderAsync(
        MountedView<SignalRow> mounted,
        Button header,
        Point? relativeStart = null,
        Point? relativeEnd = null)
    {
        var start = header.TranslatePoint(relativeStart ?? new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), mounted.Host);
        var end = header.TranslatePoint(relativeEnd ?? relativeStart ?? new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), mounted.Host);
        Assert.NotNull(start);
        Assert.NotNull(end);

        mounted.Host.MouseDown(start.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(end.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static void RaiseDoubleTapped(Control source)
    {
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var pointerArgs = new PointerPressedEventArgs(
            source,
            pointer,
            source,
            default,
            timestamp: 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);

        source.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs));
    }

}
