using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Shared.Common;

namespace Sufni.App.Shared.Views.Overlays;

public sealed class PlotZoomOverlayHost : Panel, IPlotZoomSurface
{
    private const int AnimationDurationMs = 250;
    private const double DesktopSurfaceMargin = 24;
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(AnimationDurationMs);

    public static readonly StyledProperty<UiLayoutProfile> LayoutProfileProperty =
        AvaloniaProperty.Register<PlotZoomOverlayHost, UiLayoutProfile>(
            nameof(LayoutProfile),
            UiLayoutProfile.Workspace);

    public static readonly StyledProperty<bool> HasKeyboardInputProperty =
        AvaloniaProperty.Register<PlotZoomOverlayHost, bool>(
            nameof(HasKeyboardInput),
            defaultValue: true);

    private readonly Border scrim;
    private readonly Border modalSurface;
    private readonly LayoutTransformControl rotationHost;
    private IDisposable? themeVariantSubscription;
    private PlotZoomContainer? activeContainer;
    private Control? borrowedChild;
    private TopLevel? topLevel;
    private IDisposable? pendingCloseCompletion;

    static PlotZoomOverlayHost()
    {
        LayoutProfileProperty.Changed.AddClassHandler<PlotZoomOverlayHost>((host, _) =>
        {
            if (host.IsZoomOpen)
            {
                host.ApplyModalPresentation();
            }
        });
    }

    public PlotZoomOverlayHost()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        ClipToBounds = true;
        IsVisible = false;
        IsHitTestVisible = false;

        scrim = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = SufniBrushes.OverlayScrim(),
            Opacity = 0,
        };

        rotationHost = new LayoutTransformControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        modalSurface = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = SufniBrushes.DialogSurface(),
            Child = rotationHost,
        };

        Children.Add(scrim);
        Children.Add(modalSurface);
        AddHandler(InputElement.DoubleTappedEvent, OnOverlayDoubleTapped);
    }

    public UiLayoutProfile LayoutProfile
    {
        get => GetValue(LayoutProfileProperty);
        set => SetValue(LayoutProfileProperty, value);
    }

    public bool HasKeyboardInput
    {
        get => GetValue(HasKeyboardInputProperty);
        set => SetValue(HasKeyboardInputProperty, value);
    }

    public bool IsZoomOpen => activeContainer is not null && borrowedChild is not null;

    public bool TryCollapseZoom()
    {
        if (!IsZoomOpen)
        {
            return false;
        }

        Close();
        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        topLevel = TopLevel.GetTopLevel(this);
        topLevel?.AddHandler(PlotZoomContainer.PlotZoomRequestedEvent, OnPlotZoomRequested);
        themeVariantSubscription = this.GetObservable(ThemeVariantScope.ActualThemeVariantProperty)
            .Subscribe(_ => RefreshThemeBrushes());
        RefreshThemeBrushes();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        topLevel?.RemoveHandler(PlotZoomContainer.PlotZoomRequestedEvent, OnPlotZoomRequested);
        CloseImmediately();
        themeVariantSubscription?.Dispose();
        themeVariantSubscription = null;
        topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (IsZoomOpen)
        {
            ApplyModalPresentation();
        }
    }

    private void OnPlotZoomRequested(object? sender, PlotZoomRequestedEventArgs e)
    {
        if (IsZoomOpen)
        {
            return;
        }

        Open(e.Container);
    }

    private void Open(PlotZoomContainer container)
    {
        topLevel ??= TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var sourceRect = TryGetRectInTopLevel(container);
        var child = container.BorrowChild();
        if (child is null)
        {
            return;
        }

        activeContainer = container;
        borrowedChild = child;
        rotationHost.Child = child;
        ApplyModalPresentation();

        ResetTransitions();
        scrim.Opacity = 0;
        modalSurface.RenderTransform = null;
        IsVisible = true;
        IsHitTestVisible = true;

        container.DetachedFromVisualTree += OnActiveContainerDetached;
        topLevel.AddHandler<KeyEventArgs>(
            InputElement.KeyDownEvent,
            OnTopLevelKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        Dispatcher.UIThread.Post(() => BeginOpenAnimation(sourceRect), DispatcherPriority.Loaded);
    }

    private void BeginOpenAnimation(Rect? sourceRect)
    {
        if (!IsZoomOpen)
        {
            return;
        }

        ApplyModalPresentation();
        var targetRect = TryGetRectInTopLevel(modalSurface);
        if (sourceRect is null || targetRect is null)
        {
            scrim.Opacity = 1;
            modalSurface.RenderTransform = null;
            return;
        }

        modalSurface.RenderTransformOrigin = RelativePoint.TopLeft;
        modalSurface.RenderTransform = CreateTransform(targetRect.Value, sourceRect.Value);
        modalSurface.Transitions = CreateTransformTransitions();
        scrim.Transitions = CreateOpacityTransitions();

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsZoomOpen)
            {
                return;
            }

            modalSurface.RenderTransform = CreateIdentityTransform();
            scrim.Opacity = 1;
        }, DispatcherPriority.Loaded);
    }

    private void Close()
    {
        if (!IsZoomOpen)
        {
            return;
        }

        if (activeContainer is null || borrowedChild is null)
        {
            CloseImmediately();
            return;
        }

        var sourceRect = TryGetRectInTopLevel(activeContainer);
        var targetRect = TryGetRectInTopLevel(modalSurface);
        if (sourceRect is null || targetRect is null)
        {
            CloseImmediately();
            return;
        }

        pendingCloseCompletion?.Dispose();
        pendingCloseCompletion = null;

        modalSurface.RenderTransformOrigin = RelativePoint.TopLeft;
        modalSurface.Transitions = CreateTransformTransitions();
        scrim.Transitions = CreateOpacityTransitions();
        modalSurface.RenderTransform = CreateTransform(targetRect.Value, sourceRect.Value);
        scrim.Opacity = 0;
        pendingCloseCompletion = PeriodicUiTimer.ScheduleOnce(AnimationDuration, FinishClose);
    }

    private void CloseImmediately()
    {
        if (!IsZoomOpen)
        {
            ResetClosedVisualState();
            return;
        }

        pendingCloseCompletion?.Dispose();
        pendingCloseCompletion = null;
        FinishClose();
    }

    private void FinishClose()
    {
        pendingCloseCompletion?.Dispose();
        pendingCloseCompletion = null;

        if (activeContainer is { } container)
        {
            container.DetachedFromVisualTree -= OnActiveContainerDetached;
        }

        topLevel?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);

        var returningContainer = activeContainer;
        var returningChild = borrowedChild;
        rotationHost.Child = null;
        rotationHost.LayoutTransform = null;

        if (returningContainer is not null && returningChild is not null)
        {
            returningContainer.ReturnChild(returningChild);
        }

        activeContainer = null;
        borrowedChild = null;
        ResetClosedVisualState();
    }

    private void ResetClosedVisualState()
    {
        ResetTransitions();
        scrim.Opacity = 0;
        modalSurface.RenderTransform = null;
        modalSurface.Margin = new Thickness();
        modalSurface.CornerRadius = new CornerRadius();
        modalSurface.BoxShadow = default;
        modalSurface.ClipToBounds = false;
        IsVisible = false;
        IsHitTestVisible = false;
    }

    private void OnActiveContainerDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CloseImmediately();
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !HasKeyboardInput)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void OnOverlayDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsZoomOpen)
        {
            return;
        }

        if (PlotZoomContainer.HasInteractiveControlOnRoute(e.Source, this))
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void ApplyModalPresentation()
    {
        var useWorkspacePresentation = LayoutProfile == UiLayoutProfile.Workspace;
        rotationHost.LayoutTransform = !useWorkspacePresentation && Bounds.Height > Bounds.Width
            ? new RotateTransform(90)
            : null;

        if (useWorkspacePresentation)
        {
            modalSurface.Margin = new Thickness(DesktopSurfaceMargin);
            modalSurface.CornerRadius = new CornerRadius(8);
            modalSurface.BoxShadow = BoxShadows.Parse("0 18 48 0 #66000000");
            modalSurface.ClipToBounds = true;
        }
        else
        {
            modalSurface.Margin = new Thickness();
            modalSurface.CornerRadius = new CornerRadius();
            modalSurface.BoxShadow = default;
            modalSurface.ClipToBounds = false;
        }
    }

    private void RefreshThemeBrushes()
    {
        scrim.Background = SufniBrushes.OverlayScrim();
        modalSurface.Background = SufniBrushes.DialogSurface();
    }

    private Rect? TryGetRectInTopLevel(Visual visual)
    {
        if (topLevel is null)
        {
            return null;
        }

        var origin = visual.TranslatePoint(default, topLevel);
        var size = visual.Bounds.Size;
        if (origin is null || size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        return new Rect(origin.Value, size);
    }

    private static Transitions CreateTransformTransitions() =>
    [
        new TransformOperationsTransition
        {
            Property = RenderTransformProperty,
            Duration = AnimationDuration,
            Easing = new CubicEaseOut(),
        },
    ];

    private static Transitions CreateOpacityTransitions() =>
    [
        new DoubleTransition
        {
            Property = OpacityProperty,
            Duration = AnimationDuration,
            Easing = new CubicEaseOut(),
        },
    ];

    private static TransformOperations CreateTransform(Rect from, Rect to)
    {
        var scaleX = from.Width > 0 ? to.Width / from.Width : 1;
        var scaleY = from.Height > 0 ? to.Height / from.Height : 1;
        var translateX = to.X - from.X;
        var translateY = to.Y - from.Y;
        return TransformOperations.Parse(
            FormattableString.Invariant(
                $"translate({translateX}px, {translateY}px) scale({scaleX}, {scaleY})"));
    }

    private static TransformOperations CreateIdentityTransform() =>
        TransformOperations.Parse("translate(0px, 0px) scale(1, 1)");

    private void ResetTransitions()
    {
        modalSurface.Transitions = [];
        scrim.Transitions = [];
    }
}
