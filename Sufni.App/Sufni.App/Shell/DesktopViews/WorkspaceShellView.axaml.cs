using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Sufni.App.Shared.Base;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Theming;

namespace Sufni.App.Shell.DesktopViews;

public static class TabStripMiddleClickHandler
{
    public static void Register()
    {
        InputElement.PointerPressedEvent.AddClassHandler(
            typeof(TabStripItem),
            OnPointerPressed,
            RoutingStrategies.Tunnel);
    }

    private static void OnPointerPressed(object? sender, RoutedEventArgs e)
    {
        var header = sender as Control;
        Debug.Assert(header is not null);

        var args = e as PointerPressedEventArgs;
        Debug.Assert(args is not null);

        var point = args.GetCurrentPoint(header);
        if (!point.Properties.IsMiddleButtonPressed) return;

        var vm = header.DataContext as TabPageViewModelBase;
        vm?.CloseCommand.Execute(null);
    }
}

public partial class WorkspaceShellView : UserControl
{
    private const double TabDragMovementThresholdPixels = 6;
    private const double MaxPagesPaneLength = 450;
    private const double MinPagesPaneLength = 96;
    private const double MinWorkspaceContentLength = 320;

    private TabStripItem? draggedTabItem;
    private TabPageViewModelBase? draggedTab;
    private Point tabDragStartPoint;
    private double draggedTabOriginalOpacity = 1;
    private bool isTabDragInProgress;
    private bool isTabDragFeedbackVisible;

    public WorkspaceShellView()
    {
        InitializeComponent();
        TabStripMiddleClickHandler.Register();
        SizeChanged += (_, _) => UpdateResponsivePaneLength();

        TabControl.AddHandler<PointerPressedEventArgs>(
            InputElement.PointerPressedEvent,
            OnTabPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TabControl.PointerMoved += OnTabPointerMoved;
        TabControl.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            OnTabPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Avalonia keeps per-control automation peers and their cached child lists until the
        // relevant peer recomputes its children; a removed item container's peer lingers in its
        // panel peer's children list and pins the container's DataContext -> tab VM -> telemetry.
        // Force the panel peer to recompute on removal so the dead subtree is released.
        TabControl.ContainerClearing += OnTabContainerClearing;
        TabContentHost.ContainerClearing += OnTabContainerClearing;
    }

    private static void OnTabContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        // Capture the panel now, while the container is still attached. Defer the re-read until
        // after detach + InvalidateChildren have run.
        if (e.Container.GetVisualParent() is not Control panel)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            // FromElement returns the existing peer or null; if accessibility never materialized a
            // peer there is nothing cached to leak, so this is a safe no-op.
            () => ControlAutomationPeer.FromElement(panel)?.GetChildren(),
            DispatcherPriority.Background);
    }

    private void UpdateResponsivePaneLength()
    {
        RootSplitView.OpenPaneLength = ResolveOpenPaneLength(Bounds.Width);
    }

    private static double ResolveOpenPaneLength(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return MaxPagesPaneLength;
        }

        var largestLengthLeavingContent = Math.Max(MinPagesPaneLength, width - MinWorkspaceContentLength);
        return Math.Clamp(largestLengthLeavingContent, MinPagesPaneLength, MaxPagesPaneLength);
    }

    private ShellWorkspaceViewModel? Workspace =>
        DataContext is ShellRootViewModel root ? root.Workspace : null;

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!IsPrimaryPointerPressed(args) || IsPointerOverButton(args))
        {
            return;
        }

        var tabItem = FindTabStripItem(args.Source);
        if (tabItem?.DataContext is not TabPageViewModelBase tab)
        {
            return;
        }

        draggedTabItem = tabItem;
        draggedTab = tab;
        tabDragStartPoint = args.GetPosition(TabControl);
        isTabDragInProgress = false;
        draggedTabItem.PointerCaptureLost += OnDraggedTabPointerCaptureLost;
        args.Pointer.Capture(draggedTabItem);
        args.Handled = true;
    }

    private void OnTabPointerMoved(object? sender, PointerEventArgs args)
    {
        if (draggedTab is null)
        {
            return;
        }

        if (!IsPrimaryPointerPressed(args))
        {
            args.Pointer.Capture(null);
            ResetTabDragState();
            return;
        }

        if (!isTabDragInProgress && HasExceededTabDragThreshold(args))
        {
            isTabDragInProgress = true;
            BeginTabDragFeedback(draggedTabItem);
        }

        if (!isTabDragInProgress)
        {
            return;
        }

        UpdateTabDragFeedbackAtPoint(draggedTab, args.GetPosition(TabControl));
        args.Handled = true;
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (draggedTab is null)
        {
            return;
        }

        if (isTabDragInProgress)
        {
            TryDropDraggedTab(args.GetPosition(TabControl));
            args.Handled = true;
        }
        else if (IsWithinDraggedTabBounds(args))
        {
            Workspace?.OpenOrFocus(draggedTab);
            args.Handled = true;
        }

        args.Pointer.Capture(null);
        ResetTabDragState();
    }

    private void OnDraggedTabPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        ResetTabDragState();
    }

    internal bool IsTabDragFeedbackVisible => isTabDragFeedbackVisible;

    internal bool IsTabDropIndicatorVisible => TabDropIndicator.IsVisible;

    internal double TabDropIndicatorX => TabDropIndicator.Margin.Left;

    internal void BeginTabDragFeedback(TabStripItem? tabItem)
    {
        if (tabItem is null || (ReferenceEquals(draggedTabItem, tabItem) && isTabDragFeedbackVisible))
        {
            return;
        }

        ClearTabDragFeedback();
        draggedTabItem = tabItem;
        draggedTabOriginalOpacity = tabItem.Opacity;
        tabItem.Opacity = GetDragFeedbackOpacity();
        isTabDragFeedbackVisible = true;
    }

    internal bool UpdateTabDragFeedbackAtPoint(TabPageViewModelBase tab, Point tabControlPoint)
    {
        ClearTabDropIndicator();

        if (!TryGetTabDropPreview(tab, tabControlPoint, out var preview))
        {
            return false;
        }

        ShowTabDropIndicator(preview.IndicatorX);
        return true;
    }

    internal void EndTabDragFeedback()
    {
        ClearTabDragFeedback();
        ClearTabDropIndicator();
    }

    private bool TryDropDraggedTab(Point tabControlPoint)
    {
        if (draggedTab is null ||
            Workspace is not { } workspace ||
            !TryGetTabDropPreview(draggedTab, tabControlPoint, out var preview))
        {
            return false;
        }

        return workspace.MoveTab(draggedTab, preview.TargetTab, preview.PlaceAfterTarget);
    }

    private bool TryGetTabDropPreview(
        TabPageViewModelBase tab,
        Point tabControlPoint,
        out TabDropPreview preview)
    {
        preview = default;

        if (FindTabStripItemAt(tabControlPoint) is not { DataContext: TabPageViewModelBase targetTab } targetItem ||
            ReferenceEquals(tab, targetTab))
        {
            return false;
        }

        var targetOrigin = targetItem.TranslatePoint(default, TabControl);
        if (targetOrigin is null)
        {
            return false;
        }

        var pointInTarget = tabControlPoint - targetOrigin.Value;
        var placeAfterTarget = pointInTarget.X > targetItem.Bounds.Width / 2;
        var indicatorX = placeAfterTarget
            ? targetOrigin.Value.X + targetItem.Bounds.Width
            : targetOrigin.Value.X;

        preview = new TabDropPreview(targetTab, placeAfterTarget, indicatorX);
        return true;
    }

    private TabStripItem? FindTabStripItemAt(Point tabControlPoint)
    {
        foreach (var tabItem in TabControl.GetVisualDescendants().OfType<TabStripItem>())
        {
            var origin = tabItem.TranslatePoint(default, TabControl);
            if (origin is null)
            {
                continue;
            }

            var bounds = new Rect(origin.Value, tabItem.Bounds.Size);
            if (bounds.Contains(tabControlPoint))
            {
                return tabItem;
            }
        }

        return null;
    }

    private bool HasExceededTabDragThreshold(PointerEventArgs args)
    {
        var point = args.GetPosition(TabControl);
        var delta = point - tabDragStartPoint;
        return Math.Abs(delta.X) > TabDragMovementThresholdPixels ||
               Math.Abs(delta.Y) > TabDragMovementThresholdPixels;
    }

    private bool IsWithinDraggedTabBounds(PointerEventArgs args)
    {
        if (draggedTabItem is null)
        {
            return false;
        }

        var point = args.GetPosition(draggedTabItem);
        return new Rect(0, 0, draggedTabItem.Bounds.Width, draggedTabItem.Bounds.Height).Contains(point);
    }

    private bool IsPrimaryPointerPressed(PointerEventArgs args)
    {
        return PointerGesture.IsPrimaryPressed(args, TabControl);
    }

    private void ResetTabDragState()
    {
        if (draggedTabItem is not null)
        {
            draggedTabItem.PointerCaptureLost -= OnDraggedTabPointerCaptureLost;
        }

        EndTabDragFeedback();
        draggedTabItem = null;
        draggedTab = null;
        isTabDragInProgress = false;
    }

    private void ClearTabDragFeedback()
    {
        if (isTabDragFeedbackVisible && draggedTabItem is not null)
        {
            draggedTabItem.Opacity = draggedTabOriginalOpacity;
        }

        isTabDragFeedbackVisible = false;
    }

    internal void ShowTabDropIndicator(double tabControlX)
    {
        var indicatorWidth = TabDropIndicator.Bounds.Width > 0
            ? TabDropIndicator.Bounds.Width
            : GetDropIndicatorWidth();
        TabDropIndicator.Margin = new Thickness(Math.Max(0, tabControlX - indicatorWidth / 2), 0, 0, 0);
        TabDropIndicator.IsVisible = true;
    }

    private void ClearTabDropIndicator()
    {
        TabDropIndicator.IsVisible = false;
    }

    private double GetDragFeedbackOpacity()
    {
        if (Application.Current?.TryFindResource(
                "SufniDragFeedbackOpacity",
                ActualThemeVariant,
                out var resource) == true &&
            resource is double opacity)
        {
            return opacity;
        }

        return SufniThemes.Fallback.DragDrop.FeedbackOpacity;
    }

    private double GetDropIndicatorWidth()
    {
        if (TabDropIndicator.Width > 0 && !double.IsNaN(TabDropIndicator.Width))
        {
            return TabDropIndicator.Width;
        }

        return SufniThemes.Fallback.Selection.IndicatorThickness;
    }

    private static bool IsPointerOverButton(PointerEventArgs args)
        => FindVisual<Button>(args.Source) is not null;

    private static TabStripItem? FindTabStripItem(object? source)
        => FindVisual<TabStripItem>(source);

    private static T? FindVisual<T>(object? source) where T : Visual
        => source as T ?? (source as Visual)?.FindAncestorOfType<T>();

    private readonly record struct TabDropPreview(
        TabPageViewModelBase TargetTab,
        bool PlaceAfterTarget,
        double IndicatorX);
}
