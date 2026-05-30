using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Sufni.App.KeyboardShortcuts;
using Sufni.App.Theming;
using Sufni.App.ViewModels;

namespace Sufni.App.DesktopViews;

public class IsEqualConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values.Count == 2 && values[0] == values[1];
    }
}

public class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? FontStyle.Italic : FontStyle.Normal;
        }
        return FontStyle.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToColorConverter : IMultiValueConverter
{
    private const string NormalBrushKey = "SufniTabTextBrush";
    private const string DirtyBrushKey = "SufniStatusWarningBrush";

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDirty = values.Count > 0 && values[0] is bool b && b;
        var variant = values.Count > 1 && values[1] is ThemeVariant tv
            ? tv
            : Application.Current?.ActualThemeVariant;
        var key = isDirty ? DirtyBrushKey : NormalBrushKey;
        return ResolveBrush(key, variant) ?? ResolveFallbackBrush(isDirty, variant);
    }

    private static IBrush? ResolveBrush(string key, ThemeVariant? variant)
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.TryFindResource(key, variant, out var resource) && resource is IBrush brush
            ? brush
            : null;
    }

    private static IBrush ResolveFallbackBrush(bool isDirty, ThemeVariant? variant)
    {
        var theme = SufniThemes.FromVariant(variant);
        if (isDirty && theme.Status.Warning is { } warning)
        {
            return warning.ToBrush();
        }

        return theme.Tab.Text.ToBrush();
    }
}

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

public partial class MainWindow : Window
{
    private const double TabDragMovementThresholdPixels = 6;

    private TabStripItem? draggedTabItem;
    private TabPageViewModelBase? draggedTab;
    private RawTabShortcutHandler? rawTabShortcutHandler;
    private Point tabDragStartPoint;
    private double draggedTabOriginalOpacity = 1;
    private bool isTabDragInProgress;
    private bool isTabDragFeedbackVisible;

    public MainWindow()
    {
        InitializeComponent();
        TabStripMiddleClickHandler.Register();
        rawTabShortcutHandler = RawTabShortcutHandler.Attach(TryHandleRegisteredTabShortcut);
        Closed += (_, _) => DisposeRawTabShortcutHandling();

        AddHandler<KeyEventArgs>(
            InputElement.KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
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
    }

    private void DisposeRawTabShortcutHandling()
    {
        rawTabShortcutHandler?.Dispose();
        rawTabShortcutHandler = null;
    }

    internal bool TryHandleRawTabShortcut(RawKeyEventType type, Key key, KeyModifiers modifiers) =>
        rawTabShortcutHandler?.TryHandleRawTabShortcut(type, key, modifiers) == true;

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (TryHandleRegisteredTabShortcut(args.Key, args.KeyModifiers))
        {
            args.Handled = true;
        }
    }

    internal bool TryHandleRegisteredTabShortcut(Key key, KeyModifiers modifiers)
    {
        if (DataContext is not MainWindowViewModel mainWindow)
        {
            return false;
        }

        if (MatchesMainWindowTabShortcut(key, modifiers, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab))
        {
            mainWindow.SelectNextTabCommand.Execute(null);
            return true;
        }

        if (MatchesMainWindowTabShortcut(key, modifiers, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab))
        {
            mainWindow.SelectPreviousTabCommand.Execute(null);
            return true;
        }

        return false;
    }

    private static bool MatchesMainWindowTabShortcut(Key key, KeyModifiers modifiers, string shortcutId)
    {
        var gestures = KeyboardShortcutRegistry
            .GesturesBySource[KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow][shortcutId];
        return gestures.Any(gesture => gesture.Key == Key.Tab &&
                                       gesture.Key == key &&
                                       gesture.KeyModifiers == modifiers);
    }

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
        else if (IsWithinDraggedTabBounds(args) &&
                 DataContext is MainWindowViewModel mainWindow)
        {
            mainWindow.OpenView(draggedTab);
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
            DataContext is not MainWindowViewModel mainWindow ||
            !TryGetTabDropPreview(draggedTab, tabControlPoint, out var preview))
        {
            return false;
        }

        return mainWindow.MoveTab(draggedTab, preview.TargetTab, preview.PlaceAfterTarget);
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
        var point = args.GetCurrentPoint(TabControl);
        return point.Properties.IsLeftButtonPressed || args.Pointer.Type != PointerType.Mouse;
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
