using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Sufni.App.ExtensionHost.Runtime.Presentation;

namespace Sufni.App.Tests.ExtensionHost.Runtime.Presentation;

[Collection("Ui")]
public class PlotZoomContainerTests
{
    [AvaloniaFact]
    public void BorrowChild_DetachesChildAndPinsInheritedDataContext()
    {
        var inheritedDataContext = new object();
        var child = new Border();
        var container = new PlotZoomContainer
        {
            DataContext = inheritedDataContext,
            Child = child,
        };

        var borrowed = container.BorrowChild();

        Assert.Same(child, borrowed);
        Assert.True(container.IsChildBorrowed);
        Assert.Null(container.Child);
        Assert.Same(inheritedDataContext, child.DataContext);
        Assert.True(child.IsSet(StyledElement.DataContextProperty));
    }

    [AvaloniaFact]
    public void ReturnChild_ReattachesChildAndRestoresDataContextInheritance()
    {
        var originalDataContext = new object();
        var returnedDataContext = new object();
        var child = new Border();
        var container = new PlotZoomContainer
        {
            DataContext = originalDataContext,
            Child = child,
        };

        var borrowed = Assert.IsType<Border>(container.BorrowChild());
        container.DataContext = returnedDataContext;

        container.ReturnChild(borrowed);

        Assert.False(container.IsChildBorrowed);
        Assert.Same(child, container.Child);
        Assert.False(child.IsSet(StyledElement.DataContextProperty));
        Assert.Same(returnedDataContext, child.DataContext);
    }

    [AvaloniaFact]
    public void ReturnChild_RestoresExistingLocalDataContext_WhenChildOwnedOne()
    {
        var inheritedDataContext = new object();
        var localDataContext = new object();
        var child = new Border
        {
            DataContext = localDataContext,
        };
        var container = new PlotZoomContainer
        {
            DataContext = inheritedDataContext,
            Child = child,
        };

        var borrowed = Assert.IsType<Border>(container.BorrowChild());
        child.DataContext = new object();

        container.ReturnChild(borrowed);

        Assert.False(container.IsChildBorrowed);
        Assert.Same(child, container.Child);
        Assert.True(child.IsSet(StyledElement.DataContextProperty));
        Assert.Same(localDataContext, child.DataContext);
    }

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

    [AvaloniaFact]
    public void DoubleTap_DoesNotRaise_WhileChildBorrowed()
    {
        var container = new PlotZoomContainer
        {
            Child = new Border(),
        };
        var requestCount = 0;
        container.AddHandler(
            PlotZoomContainer.PlotZoomRequestedEvent,
            (_, _) => requestCount++);

        Assert.NotNull(container.BorrowChild());
        RaiseDoubleTapped(container);

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
