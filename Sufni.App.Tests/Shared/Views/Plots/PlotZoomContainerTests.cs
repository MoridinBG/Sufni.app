using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Sufni.App.Shared.Views.Plots;

namespace Sufni.App.Tests.Shared.Views.Plots;

[Collection("Ui")]
public class PlotZoomContainerTests
{

    [AvaloniaFact]
    public void DoubleTap_RaisesPlotZoomRequested_AndBubblesToAncestor()
    {
        var container = new PlotZoomContainer
        {
            Child = new Border(),
        };
        var ancestor = new Border
        {
            Child = container,
        };
        PlotZoomRequestedEventArgs? received = null;
        ancestor.AddHandler(
            PlotZoomContainer.PlotZoomRequestedEvent,
            (_, args) => received = args);

        RaiseDoubleTapped(container);

        Assert.NotNull(received);
        Assert.Same(container, received.Container);
    }

    [AvaloniaFact]
    public void DoubleTap_DoesNotRaise_WhenSourceIsInteractiveDescendant()
    {
        var button = new Button();
        var container = new PlotZoomContainer
        {
            Child = button,
        };
        var requestCount = 0;
        container.AddHandler(
            PlotZoomContainer.PlotZoomRequestedEvent,
            (_, _) => requestCount++);

        RaiseDoubleTapped(button);

        Assert.Equal(0, requestCount);
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
