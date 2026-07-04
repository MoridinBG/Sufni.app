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
    public async Task ZoomRequest_MovesChildIntoOverlay_AndEnablesOverlayHitTesting()
    {
        await using var mounted = await MountAsync();

        await OpenAsync(mounted.Container);

        Assert.True(mounted.Overlay.IsZoomOpen);
        Assert.True(mounted.Overlay.IsVisible);
        Assert.True(mounted.Overlay.IsHitTestVisible);
        Assert.True(mounted.Container.IsChildBorrowed);
        Assert.Null(mounted.Container.Child);
        Assert.Same(mounted.Child, GetRotationHost(mounted.Overlay).Child);
    }

    [AvaloniaFact]
    public async Task DoubleTapInsideOverlay_ClosesAndReturnsChild()
    {
        await using var mounted = await MountAsync();
        await OpenAsync(mounted.Container);
        await ForceImmediateClosePathAsync(mounted);

        RaiseDoubleTapped(mounted.Overlay);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.False(mounted.Overlay.IsVisible);
        Assert.False(mounted.Container.IsChildBorrowed);
        Assert.Same(mounted.Child, mounted.Container.Child);
        Assert.Null(GetRotationHost(mounted.Overlay).Child);
    }

    [AvaloniaFact]
    public async Task EscapeKey_ClosesOverlay()
    {
        TestApp.SetIsDesktop(true);
        await using var mounted = await MountAsync();
        await OpenAsync(mounted.Container);
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = mounted.Host,
            Key = Key.Escape,
        };

        await ForceImmediateClosePathAsync(mounted);
        mounted.Host.RaiseEvent(args);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(args.Handled);
        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.Same(mounted.Child, mounted.Container.Child);
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
    public async Task Open_AppliesRotation_WhenMobileAndPortrait()
    {
        TestApp.SetIsDesktop(false);
        try
        {
            await using var mounted = await MountAsync(width: 320, height: 640);

            await OpenAsync(mounted.Container);

            Assert.IsType<RotateTransform>(GetRotationHost(mounted.Overlay).LayoutTransform);
        }
        finally
        {
            TestApp.SetIsDesktop(true);
        }
    }

    [AvaloniaFact]
    public async Task Open_NeverRotates_WhenDesktop()
    {
        TestApp.SetIsDesktop(true);
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
        await ForceImmediateClosePathAsync(mounted);
        Assert.True(mounted.Overlay.TryCollapseZoom());
        await ViewTestHelpers.FlushDispatcherAsync();

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
    }

    private static async Task ForceImmediateClosePathAsync(MountedOverlay mounted)
    {
        mounted.Container.Width = 0;
        mounted.Container.Height = 0;
        mounted.Root.Measure(new Size(mounted.Host.Width, mounted.Host.Height));
        mounted.Root.Arrange(new Rect(0, 0, mounted.Host.Width, mounted.Host.Height));
        await ViewTestHelpers.FlushDispatcherAsync();
    }

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
