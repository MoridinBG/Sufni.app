using Sufni.App.Tests.TestSupport.Async;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shared.Views.Overlays;

[Collection("Ui")]
public class PlotZoomOverlayHostTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZoomRequest_MovesChildIntoOverlay_AndEnablesOverlayHitTesting(bool localContext)
    {
        using var timers = new ManualUiTimerScheduler();
        await using var mounted = await MountAsync(scheduler: timers);
        mounted.Container.DataContext = "Original";
        if (localContext) mounted.Child.DataContext = "Local";
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("."));
        mounted.Child.Child = text;
        await ViewTestHelpers.FlushDispatcherAsync();

        await OpenAsync(mounted.Container);
        Assert.True(mounted.Overlay.IsVisible);
        Assert.True(mounted.Overlay.IsHitTestVisible);
        Assert.True(text.IsEffectivelyVisible);
        Assert.Equal(localContext ? "Local" : "Original", text.Text);
        mounted.Container.DataContext = "Updated";
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(localContext ? "Local" : "Original", text.Text);

        Assert.True(mounted.Overlay.TryCollapseZoom());
        timers.FireAll();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.Overlay.IsVisible);
        Assert.True(text.IsEffectivelyVisible);
        Assert.Equal(localContext ? "Local" : "Updated", text.Text);
    }

    [AvaloniaFact]
    public async Task DoubleTapInsideOverlay_ClosesAndReturnsChild()
    {
        using var timers = new ManualUiTimerScheduler();
        await using var mounted = await MountAsync(scheduler: timers);
        await OpenAsync(mounted.Container);

        RaiseDoubleTapped(mounted.Overlay);
        timers.FireAll();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.False(mounted.Overlay.IsVisible);
        Assert.False(mounted.Container.IsChildBorrowed);
        Assert.Same(mounted.Child, mounted.Container.Child);
    }

    [AvaloniaFact]
    public async Task EscapeKey_ClosesOverlay()
    {
        using var timers = new ManualUiTimerScheduler();
        await using var mounted = await MountAsync(scheduler: timers);
        await OpenAsync(mounted.Container);
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = mounted.Host,
            Key = Key.Escape,
        };

        mounted.Host.RaiseEvent(args);
        timers.FireAll();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(args.Handled);
        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.Same(mounted.Child, mounted.Container.Child);
    }

    [AvaloniaFact]
    public async Task EscapeKey_Ignored_WhenInputHasNoKeyboard()
    {
        await using var mounted = await MountAsync(hasKeyboardInput: false);
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
    public async Task TryCollapseZoom_ReturnsTrueOnlyWhenOpen()
    {
        using var timers = new ManualUiTimerScheduler();
        await using var mounted = await MountAsync(scheduler: timers);

        Assert.False(mounted.Overlay.TryCollapseZoom());

        await OpenAsync(mounted.Container);
        Assert.True(mounted.Overlay.TryCollapseZoom());
        timers.FireAll();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.Overlay.IsZoomOpen);
        Assert.False(mounted.Overlay.TryCollapseZoom());
    }

    [AvaloniaFact]
    public async Task CloseAnimation_ReturnsEachChild_WhenItsRootSchedulerCompletes()
    {
        using var firstTimers = new ManualUiTimerScheduler();
        using var secondTimers = new ManualUiTimerScheduler();
        await using var first = await MountAsync(scheduler: firstTimers);
        await using var second = await MountAsync(scheduler: secondTimers);
        await OpenAsync(first.Container);
        await OpenAsync(second.Container);

        Assert.True(first.Overlay.TryCollapseZoom());
        Assert.True(second.Overlay.TryCollapseZoom());
        Assert.Null(first.Container.Child);
        Assert.Null(second.Container.Child);

        firstTimers.FireAll();

        Assert.False(first.Overlay.IsZoomOpen);
        Assert.Same(first.Child, first.Container.Child);
        Assert.True(second.Overlay.IsZoomOpen);
        Assert.Null(second.Container.Child);

        secondTimers.FireAll();

        Assert.False(second.Overlay.IsZoomOpen);
        Assert.Same(second.Child, second.Container.Child);
    }

    private static async Task<MountedOverlay> MountAsync(
        double width = 640,
        double height = 360,
        UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace,
        bool hasKeyboardInput = true,
        IUiTimerScheduler? scheduler = null)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var child = new Border();
        var container = new PlotZoomContainer
        {
            Child = child,
        };
        var overlay = new PlotZoomOverlayHost
        {
            LayoutProfile = layoutProfile,
            HasKeyboardInput = hasKeyboardInput,
        };
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

        ViewTestHelpers.ShowWindow(host, scheduler);
        await ViewTestHelpers.FlushDispatcherAsync();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedOverlay(host, root, container, overlay, child);
    }

    private static async Task OpenAsync(PlotZoomContainer container)
    {
        RaiseDoubleTapped(container);
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
