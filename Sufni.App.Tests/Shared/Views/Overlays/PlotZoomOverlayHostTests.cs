using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shared.Views.Overlays;

[Collection("Ui")]
public class PlotZoomOverlayHostTests
{
    [AvaloniaFact]
    public async Task ZoomRequest_MovesChildIntoOverlay_AndShowsScrim()
    {
        await using var mounted = await MountAsync();

        await OpenAsync(mounted.Container);

        Assert.True(mounted.Overlay.IsZoomOpen);
        Assert.True(mounted.Overlay.IsVisible);
        Assert.True(mounted.Overlay.IsHitTestVisible);
        Assert.True(mounted.Container.IsChildBorrowed);
        Assert.Null(mounted.Container.Child);
        Assert.Same(mounted.Child, GetRotationHost(mounted.Overlay).Child);
        Assert.Equal(1, GetScrim(mounted.Overlay).Opacity);
    }

    [AvaloniaFact]
    public async Task DoubleTapInsideOverlay_ClosesAndReturnsChild()
    {
        await using var mounted = await MountAsync();
        await OpenAsync(mounted.Container);

        RaiseDoubleTapped(mounted.Overlay);
        await WaitForCloseAnimationAsync();

        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.False(mounted.Overlay.IsVisible);
        Assert.False(mounted.Container.IsChildBorrowed);
        Assert.Same(mounted.Child, mounted.Container.Child);
        Assert.Null(GetRotationHost(mounted.Overlay).Child);
    }

    [AvaloniaFact]
    public async Task EscapeKey_ClosesOverlay()
    {
        using var environment = TestApp.UsePointerInput();
        await using var mounted = await MountAsync();
        await OpenAsync(mounted.Container);
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = mounted.Host,
            Key = Key.Escape,
        };

        mounted.Host.RaiseEvent(args);
        await WaitForCloseAnimationAsync();

        Assert.True(args.Handled);
        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.Same(mounted.Child, mounted.Container.Child);
    }

    [AvaloniaFact]
    public async Task EscapeKey_Ignored_WhenInputHasNoKeyboard()
    {
        using var environment = TestApp.UseTouchInput();
        await using var mounted = await MountAsync();
        await OpenAsync(mounted.Container);
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = mounted.Host,
            Key = Key.Escape,
        };

        mounted.Host.RaiseEvent(args);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(args.Handled);
        Assert.True(mounted.Overlay.IsZoomOpen);
    }

    [AvaloniaFact]
    public async Task ContainerDetachedWhileZoomed_ClosesImmediatelyAndReturnsChild()
    {
        await using var mounted = await MountAsync();
        await OpenAsync(mounted.Container);

        mounted.Root.Children.Remove(mounted.Container);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.False(mounted.Overlay.IsVisible);
        Assert.False(mounted.Container.IsChildBorrowed);
        Assert.Same(mounted.Child, mounted.Container.Child);
    }

    [AvaloniaFact]
    public async Task Open_AppliesRotation_WhenCompactAndPortrait()
    {
        using var environment = TestApp.UseTouchInput();
        await using var mounted = await MountAsync(width: 320, height: 640);

        await OpenAsync(mounted.Container);

        Assert.IsType<RotateTransform>(GetRotationHost(mounted.Overlay).LayoutTransform);
    }

    [AvaloniaFact]
    public async Task Open_NeverRotates_WhenWorkspace()
    {
        using var environment = TestApp.UsePointerInput();
        await using var mounted = await MountAsync(width: 320, height: 640);

        await OpenAsync(mounted.Container);

        Assert.Null(GetRotationHost(mounted.Overlay).LayoutTransform);
    }

    [AvaloniaFact]
    public async Task TryCollapseZoom_ReturnsTrueOnlyWhenOpen()
    {
        await using var mounted = await MountAsync();

        Assert.False(mounted.Overlay.TryCollapseZoom());

        await OpenAsync(mounted.Container);
        Assert.True(mounted.Overlay.TryCollapseZoom());
        await WaitForCloseAnimationAsync();

        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.False(mounted.Overlay.TryCollapseZoom());
    }

    private static async Task<MountedOverlay> MountAsync(double width = 640, double height = 360)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var child = new Border();
        var container = new PlotZoomContainer
        {
            Child = child,
        };
        var overlay = new PlotZoomOverlayHost();
        var root = new Grid
        {
            Children =
            {
                container,
                overlay,
            },
        };
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
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedOverlay(host, root, container, overlay, child);
    }

    private static async Task OpenAsync(PlotZoomContainer container)
    {
        container.RaiseEvent(new PlotZoomRequestedEventArgs(container));
        await ViewTestHelpers.FlushDispatcherAsync();
        await ViewTestHelpers.FlushDispatcherAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static async Task WaitForCloseAnimationAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static Border GetScrim(PlotZoomOverlayHost overlay) =>
        Assert.IsType<Border>(overlay.Children[0]);

    private static LayoutTransformControl GetRotationHost(PlotZoomOverlayHost overlay) =>
        Assert.Single(overlay.GetVisualDescendants().OfType<LayoutTransformControl>());

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

    private sealed class MountedOverlay(
        Window host,
        Grid root,
        PlotZoomContainer container,
        PlotZoomOverlayHost overlay,
        Border child) : IAsyncDisposable
    {
        public Window Host { get; } = host;
        public Grid Root { get; } = root;
        public PlotZoomContainer Container { get; } = container;
        public PlotZoomOverlayHost Overlay { get; } = overlay;
        public Border Child { get; } = child;

        public async ValueTask DisposeAsync()
        {
            Host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
