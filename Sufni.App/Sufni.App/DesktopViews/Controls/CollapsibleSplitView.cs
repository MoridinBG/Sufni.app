using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Sufni.App.Models;

namespace Sufni.App.DesktopViews.Controls;

public sealed class CollapsibleSplitView : UserControl
{
    private const double SplitHandleThickness = 3;
    private const double DefaultCollapseThresholdRatio = 0.05;
    private const double DefaultCollapsedHeaderThickness = 34;
    private const double CollapsedHeaderIconSize = 14;
    private const string ChevronRightPath = "M10 6L8.59 7.41L13.17 12L8.59 16.59L10 18L16 12Z";
    private const string ChevronLeftPath = "M14 6L15.41 7.41L10.83 12L15.41 16.59L14 18L8 12Z";
    private const string ChevronDownPath = "M6 10L7.41 8.59L12 13.17L16.59 8.59L18 10L12 16Z";
    private const string ChevronUpPath = "M6 14L7.41 15.41L12 10.83L16.59 15.41L18 14L12 8Z";

    private readonly Grid layoutGrid = new();
    private readonly Grid firstPaneRoot = new();
    private readonly Grid secondPaneRoot = new();
    private readonly ContentControl firstContentHost = new() { Name = "PART_FirstContentHost" };
    private readonly ContentControl secondContentHost = new() { Name = "PART_SecondContentHost" };
    private readonly Button firstCollapsedHeader = CreateCollapsedHeader("PART_FirstCollapsedHeader");
    private readonly Button secondCollapsedHeader = CreateCollapsedHeader("PART_SecondCollapsedHeader");
    private readonly Border splitHandle = new() { Name = "PART_SplitHandle" };

    private bool applyingPreferences;
    private bool publishingPreferences;
    private bool isFirstPaneCollapsed;
    private bool isSecondPaneCollapsed;
    private bool isDragging;
    private double currentFirstRatio = 0.5;
    private double currentSecondRatio = 0.5;
    private double dragStartPosition;
    private double dragStartFirstSize;
    private double dragStartSecondSize;
    private double dragStartFirstRatio = 0.5;
    private double dragStartSecondRatio = 0.5;

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, Orientation>(
            nameof(Orientation),
            Orientation.Horizontal);

    public static readonly StyledProperty<string> FirstPaneIdProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, string>(
            nameof(FirstPaneId),
            "");

    public static readonly StyledProperty<string> SecondPaneIdProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, string>(
            nameof(SecondPaneId),
            "");

    public static readonly StyledProperty<string> FirstPaneLabelProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, string>(
            nameof(FirstPaneLabel),
            "");

    public static readonly StyledProperty<string> SecondPaneLabelProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, string>(
            nameof(SecondPaneLabel),
            "");

    public static readonly StyledProperty<Control?> FirstContentProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, Control?>(nameof(FirstContent));

    public static readonly StyledProperty<Control?> SecondContentProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, Control?>(nameof(SecondContent));

    public static readonly StyledProperty<bool> HasFirstPaneProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, bool>(
            nameof(HasFirstPane),
            defaultValue: true);

    public static readonly StyledProperty<bool> HasSecondPaneProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, bool>(
            nameof(HasSecondPane),
            defaultValue: true);

    public static readonly StyledProperty<bool> CanCollapseFirstPaneProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, bool>(
            nameof(CanCollapseFirstPane),
            defaultValue: true);

    public static readonly StyledProperty<bool> CanCollapseSecondPaneProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, bool>(
            nameof(CanCollapseSecondPane),
            defaultValue: true);

    public static readonly StyledProperty<GridLength> DefaultFirstLengthProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, GridLength>(
            nameof(DefaultFirstLength),
            new GridLength(1, GridUnitType.Star));

    public static readonly StyledProperty<GridLength> DefaultSecondLengthProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, GridLength>(
            nameof(DefaultSecondLength),
            new GridLength(1, GridUnitType.Star));

    public static readonly StyledProperty<SessionPaneGroupPreferences?> PreferencesProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, SessionPaneGroupPreferences?>(
            nameof(Preferences),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> CollapseThresholdRatioProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, double>(
            nameof(CollapseThresholdRatio),
            defaultValue: DefaultCollapseThresholdRatio);

    public static readonly StyledProperty<double> CollapsedHeaderThicknessProperty =
        AvaloniaProperty.Register<CollapsibleSplitView, double>(
            nameof(CollapsedHeaderThickness),
            defaultValue: DefaultCollapsedHeaderThickness);

    static CollapsibleSplitView()
    {
        OrientationProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        FirstPaneIdProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        SecondPaneIdProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        FirstPaneLabelProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.UpdateCollapsedHeaderContent());
        SecondPaneLabelProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.UpdateCollapsedHeaderContent());
        FirstContentProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.UpdateContentHosts());
        SecondContentProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.UpdateContentHosts());
        HasFirstPaneProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        HasSecondPaneProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        CanCollapseFirstPaneProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        CanCollapseSecondPaneProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        DefaultFirstLengthProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        DefaultSecondLengthProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        PreferencesProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) =>
        {
            if (!view.publishingPreferences)
            {
                view.ApplyPreferencesOrDefaults();
            }
        });
        CollapseThresholdRatioProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
        CollapsedHeaderThicknessProperty.Changed.AddClassHandler<CollapsibleSplitView>((view, _) => view.ApplyPreferencesOrDefaults());
    }

    public CollapsibleSplitView()
    {
        firstPaneRoot.Children.Add(firstContentHost);
        firstPaneRoot.Children.Add(firstCollapsedHeader);
        secondPaneRoot.Children.Add(secondContentHost);
        secondPaneRoot.Children.Add(secondCollapsedHeader);

        layoutGrid.Children.Add(firstPaneRoot);
        layoutGrid.Children.Add(splitHandle);
        layoutGrid.Children.Add(secondPaneRoot);
        Content = layoutGrid;

        splitHandle[!Border.BackgroundProperty] = new DynamicResourceExtension("SufniSplitterSurface");
        splitHandle.PointerPressed += OnSplitHandlePointerPressed;
        splitHandle.PointerMoved += OnSplitHandlePointerMoved;
        splitHandle.PointerReleased += OnSplitHandlePointerReleased;
        splitHandle.PointerCaptureLost += OnSplitHandlePointerCaptureLost;
        splitHandle.AddHandler<TappedEventArgs>(
            InputElement.DoubleTappedEvent,
            (_, args) =>
            {
                ResetToDefaultsAndPublish();
                args.Handled = true;
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        firstCollapsedHeader.Click += (_, _) => ExpandCollapsedPane(firstPane: true);
        secondCollapsedHeader.Click += (_, _) => ExpandCollapsedPane(firstPane: false);
        UpdateContentHosts();
        ApplyPreferencesOrDefaults();
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public string FirstPaneId
    {
        get => GetValue(FirstPaneIdProperty);
        set => SetValue(FirstPaneIdProperty, value);
    }

    public string SecondPaneId
    {
        get => GetValue(SecondPaneIdProperty);
        set => SetValue(SecondPaneIdProperty, value);
    }

    public string FirstPaneLabel
    {
        get => GetValue(FirstPaneLabelProperty);
        set => SetValue(FirstPaneLabelProperty, value);
    }

    public string SecondPaneLabel
    {
        get => GetValue(SecondPaneLabelProperty);
        set => SetValue(SecondPaneLabelProperty, value);
    }

    public Control? FirstContent
    {
        get => GetValue(FirstContentProperty);
        set => SetValue(FirstContentProperty, value);
    }

    public Control? SecondContent
    {
        get => GetValue(SecondContentProperty);
        set => SetValue(SecondContentProperty, value);
    }

    public bool HasFirstPane
    {
        get => GetValue(HasFirstPaneProperty);
        set => SetValue(HasFirstPaneProperty, value);
    }

    public bool HasSecondPane
    {
        get => GetValue(HasSecondPaneProperty);
        set => SetValue(HasSecondPaneProperty, value);
    }

    public bool CanCollapseFirstPane
    {
        get => GetValue(CanCollapseFirstPaneProperty);
        set => SetValue(CanCollapseFirstPaneProperty, value);
    }

    public bool CanCollapseSecondPane
    {
        get => GetValue(CanCollapseSecondPaneProperty);
        set => SetValue(CanCollapseSecondPaneProperty, value);
    }

    public GridLength DefaultFirstLength
    {
        get => GetValue(DefaultFirstLengthProperty);
        set => SetValue(DefaultFirstLengthProperty, value);
    }

    public GridLength DefaultSecondLength
    {
        get => GetValue(DefaultSecondLengthProperty);
        set => SetValue(DefaultSecondLengthProperty, value);
    }

    public SessionPaneGroupPreferences? Preferences
    {
        get => GetValue(PreferencesProperty);
        set => SetValue(PreferencesProperty, value);
    }

    public double CollapseThresholdRatio
    {
        get => GetValue(CollapseThresholdRatioProperty);
        set => SetValue(CollapseThresholdRatioProperty, value);
    }

    public double CollapsedHeaderThickness
    {
        get => GetValue(CollapsedHeaderThicknessProperty);
        set => SetValue(CollapsedHeaderThicknessProperty, value);
    }

    internal bool IsFirstPaneCollapsed => isFirstPaneCollapsed;
    internal bool IsSecondPaneCollapsed => isSecondPaneCollapsed;
    internal bool IsDragging => isDragging;

    internal void ResetToDefaultsForTests() => ResetToDefaultsAndPublish();

    internal SessionPaneGroupPreferences? CaptureCurrentPreferences()
    {
        var panes = new List<SessionPaneSizePreference>(capacity: 2);
        if (HasFirstPane && !string.IsNullOrWhiteSpace(FirstPaneId))
        {
            panes.Add(new SessionPaneSizePreference(FirstPaneId, currentFirstRatio, isFirstPaneCollapsed));
        }

        if (HasSecondPane && !string.IsNullOrWhiteSpace(SecondPaneId))
        {
            panes.Add(new SessionPaneSizePreference(SecondPaneId, currentSecondRatio, isSecondPaneCollapsed));
        }

        return panes.Count > 0 ? new SessionPaneGroupPreferences(panes) : null;
    }

    internal void BeginDragForTests()
    {
        isDragging = true;
        dragStartFirstRatio = currentFirstRatio;
        dragStartSecondRatio = currentSecondRatio;
    }

    internal void DragToFirstRatioForTests(double firstRatio)
    {
        var normalizedFirstRatio = Math.Clamp(firstRatio, 0, 1);
        var firstDelta = normalizedFirstRatio - currentFirstRatio;
        ApplyDragCandidate(normalizedFirstRatio, 1 - normalizedFirstRatio, firstDelta);
    }

    internal void CompleteDragForTests()
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        PublishCurrentState();
    }

    private static Button CreateCollapsedHeader(string name)
    {
        var button = new Button
        {
            Name = name,
            Padding = new Thickness(6, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0,
        };

        button[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("SufniSplitterSurface");
        button[!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension("SufniTabText");
        button[!TemplatedControl.BorderBrushProperty] = new DynamicResourceExtension("SufniFieldBorder");
        return button;
    }

    private void UpdateContentHosts()
    {
        firstContentHost.Content = !isFirstPaneCollapsed ? FirstContent : null;
        secondContentHost.Content = !isSecondPaneCollapsed ? SecondContent : null;
    }

    private void ApplyPreferencesOrDefaults()
    {
        if (applyingPreferences)
        {
            return;
        }

        applyingPreferences = true;
        try
        {
            if (ApplySinglePaneLayoutIfNeeded())
            {
                return;
            }

            if (TryGetVisiblePaneIds(out var paneIds) &&
                Preferences is { } preferences &&
                preferences.TryGetPaneStates(paneIds, out var states))
            {
                ApplyStoredPaneStates(states);
                return;
            }

            ApplyDefaultLayout();
        }
        finally
        {
            applyingPreferences = false;
        }
    }

    private bool TryGetVisiblePaneIds(out IReadOnlyList<string> paneIds)
    {
        paneIds = [];
        if (!HasFirstPane || !HasSecondPane ||
            string.IsNullOrWhiteSpace(FirstPaneId) ||
            string.IsNullOrWhiteSpace(SecondPaneId))
        {
            return false;
        }

        paneIds = [FirstPaneId, SecondPaneId];
        return true;
    }

    private bool ApplySinglePaneLayoutIfNeeded()
    {
        if (HasFirstPane && HasSecondPane)
        {
            return false;
        }

        isFirstPaneCollapsed = false;
        isSecondPaneCollapsed = false;
        currentFirstRatio = HasFirstPane ? 1 : 0;
        currentSecondRatio = HasSecondPane ? 1 : 0;

        if (HasFirstPane)
        {
            ApplyTrackLengths(new GridLength(1, GridUnitType.Star), new GridLength(0), handleVisible: false);
        }
        else if (HasSecondPane)
        {
            ApplyTrackLengths(new GridLength(0), new GridLength(1, GridUnitType.Star), handleVisible: false);
        }
        else
        {
            ApplyTrackLengths(new GridLength(0), new GridLength(0), handleVisible: false);
        }

        UpdateVisualStates();
        return true;
    }

    private void ApplyStoredPaneStates(IReadOnlyList<SessionPaneStatePreference> states)
    {
        var first = states[0];
        var second = states[1];
        var total = first.Ratio + second.Ratio;
        if (!double.IsFinite(total) || total <= 0)
        {
            ApplyDefaultLayout();
            return;
        }

        var normalizedFirst = first.Ratio / total;
        var normalizedSecond = second.Ratio / total;
        var threshold = GetCollapseThresholdRatio();
        if (IsAtOrBelowCollapseThreshold(normalizedFirst, threshold) ||
            IsAtOrBelowCollapseThreshold(normalizedSecond, threshold))
        {
            ApplyDefaultLayout();
            return;
        }

        var firstCollapsed = first.IsCollapsed && CanCollapseFirstPane;
        var secondCollapsed = second.IsCollapsed && CanCollapseSecondPane;
        if (firstCollapsed && secondCollapsed)
        {
            firstCollapsed = false;
            secondCollapsed = false;
        }

        ApplyPaneState(normalizedFirst, normalizedSecond, firstCollapsed, secondCollapsed);
    }

    private void ApplyDefaultLayout()
    {
        var (firstRatio, secondRatio) = GetDefaultRatios();
        isFirstPaneCollapsed = false;
        isSecondPaneCollapsed = false;
        currentFirstRatio = firstRatio;
        currentSecondRatio = secondRatio;
        ApplyTrackLengths(DefaultFirstLength, DefaultSecondLength, handleVisible: HasFirstPane && HasSecondPane);
        UpdateVisualStates();
    }

    private void ApplyPaneState(
        double firstRatio,
        double secondRatio,
        bool firstCollapsed,
        bool secondCollapsed)
    {
        var (normalizedFirst, normalizedSecond) = NormalizeRatios(firstRatio, secondRatio);
        isFirstPaneCollapsed = firstCollapsed;
        isSecondPaneCollapsed = secondCollapsed;
        currentFirstRatio = normalizedFirst;
        currentSecondRatio = normalizedSecond;

        if (isFirstPaneCollapsed)
        {
            ApplyTrackLengths(
                new GridLength(GetCollapsedHeaderThickness()),
                new GridLength(1, GridUnitType.Star),
                handleVisible: false);
        }
        else if (isSecondPaneCollapsed)
        {
            ApplyTrackLengths(
                new GridLength(1, GridUnitType.Star),
                new GridLength(GetCollapsedHeaderThickness()),
                handleVisible: false);
        }
        else
        {
            ApplyTrackLengths(
                new GridLength(normalizedFirst, GridUnitType.Star),
                new GridLength(normalizedSecond, GridUnitType.Star),
                handleVisible: HasFirstPane && HasSecondPane);
        }

        UpdateVisualStates();
    }

    private void ApplyTrackLengths(GridLength firstLength, GridLength secondLength, bool handleVisible)
    {
        layoutGrid.ColumnDefinitions.Clear();
        layoutGrid.RowDefinitions.Clear();

        var handleLength = handleVisible ? new GridLength(SplitHandleThickness) : new GridLength(0);
        if (Orientation == Orientation.Horizontal)
        {
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(firstLength));
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(handleLength));
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(secondLength));
            layoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            Grid.SetColumn(firstPaneRoot, 0);
            Grid.SetRow(firstPaneRoot, 0);
            Grid.SetColumn(splitHandle, 1);
            Grid.SetRow(splitHandle, 0);
            Grid.SetColumn(secondPaneRoot, 2);
            Grid.SetRow(secondPaneRoot, 0);
            splitHandle.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }
        else
        {
            layoutGrid.RowDefinitions.Add(new RowDefinition(firstLength));
            layoutGrid.RowDefinitions.Add(new RowDefinition(handleLength));
            layoutGrid.RowDefinitions.Add(new RowDefinition(secondLength));
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            Grid.SetColumn(firstPaneRoot, 0);
            Grid.SetRow(firstPaneRoot, 0);
            Grid.SetColumn(splitHandle, 0);
            Grid.SetRow(splitHandle, 1);
            Grid.SetColumn(secondPaneRoot, 0);
            Grid.SetRow(secondPaneRoot, 2);
            splitHandle.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }

        splitHandle.IsVisible = handleVisible;
        splitHandle.IsHitTestVisible = handleVisible;
    }

    private void UpdateVisualStates()
    {
        UpdateCollapsedHeaderContent();

        firstPaneRoot.IsVisible = HasFirstPane;
        secondPaneRoot.IsVisible = HasSecondPane;
        UpdateContentHosts();
        firstContentHost.IsVisible = HasFirstPane && !isFirstPaneCollapsed;
        secondContentHost.IsVisible = HasSecondPane && !isSecondPaneCollapsed;
        firstCollapsedHeader.IsVisible = HasFirstPane && isFirstPaneCollapsed;
        secondCollapsedHeader.IsVisible = HasSecondPane && isSecondPaneCollapsed;
    }

    private void UpdateCollapsedHeaderContent()
    {
        firstCollapsedHeader.Content = Orientation == Orientation.Horizontal
            ? CreateCollapsedHeaderIcon(ChevronRightPath)
            : CreateCollapsedHeaderIcon(ChevronDownPath);
        secondCollapsedHeader.Content = Orientation == Orientation.Horizontal
            ? CreateCollapsedHeaderIcon(ChevronLeftPath)
            : CreateCollapsedHeaderIcon(ChevronUpPath);

        var firstLabel = string.IsNullOrWhiteSpace(FirstPaneLabel) ? "pane" : FirstPaneLabel;
        var secondLabel = string.IsNullOrWhiteSpace(SecondPaneLabel) ? "pane" : SecondPaneLabel;
        ToolTip.SetTip(firstCollapsedHeader, $"Expand {firstLabel}");
        ToolTip.SetTip(secondCollapsedHeader, $"Expand {secondLabel}");
    }

    private static PathIcon CreateCollapsedHeaderIcon(string pathData)
    {
        var icon = new PathIcon
        {
            Width = CollapsedHeaderIconSize,
            Height = CollapsedHeaderIconSize,
            Data = Geometry.Parse(pathData),
        };
        icon[!PathIcon.ForegroundProperty] = new DynamicResourceExtension("SufniTabText");
        return icon;
    }

    private void ExpandCollapsedPane(bool firstPane)
    {
        if (firstPane && (!HasFirstPane || !isFirstPaneCollapsed))
        {
            return;
        }

        if (!firstPane && (!HasSecondPane || !isSecondPaneCollapsed))
        {
            return;
        }

        if (!HasFirstPane || !HasSecondPane)
        {
            ApplySinglePaneLayoutIfNeeded();
            PublishCurrentState();
            return;
        }

        ApplyPaneState(currentFirstRatio, currentSecondRatio, firstCollapsed: false, secondCollapsed: false);
        PublishCurrentState();
    }

    private void ResetToDefaultsAndPublish()
    {
        ApplyDefaultLayout();
        PublishCurrentState();
    }

    private void PublishCurrentState()
    {
        if (applyingPreferences)
        {
            return;
        }

        var nextPreferences = CaptureCurrentPreferences();
        publishingPreferences = true;
        try
        {
            Preferences = nextPreferences;
        }
        finally
        {
            publishingPreferences = false;
        }
    }

    private void OnSplitHandlePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!HasFirstPane || !HasSecondPane || isFirstPaneCollapsed || isSecondPaneCollapsed)
        {
            return;
        }

        var point = args.GetPosition(this);
        dragStartPosition = Orientation == Orientation.Horizontal ? point.X : point.Y;
        dragStartFirstSize = GetPaneLength(firstPaneRoot);
        dragStartSecondSize = GetPaneLength(secondPaneRoot);
        if (dragStartFirstSize <= 0 || dragStartSecondSize <= 0)
        {
            var (firstRatio, secondRatio) = GetDefaultRatios();
            var availableLength = GetAvailableLength();
            dragStartFirstSize = availableLength * firstRatio;
            dragStartSecondSize = availableLength * secondRatio;
        }

        (dragStartFirstRatio, dragStartSecondRatio) = NormalizeRatios(dragStartFirstSize, dragStartSecondSize);
        isDragging = true;
        args.Pointer.Capture(splitHandle);
        args.Handled = true;
    }

    private void OnSplitHandlePointerMoved(object? sender, PointerEventArgs args)
    {
        if (!isDragging)
        {
            return;
        }

        var point = args.GetPosition(this);
        var position = Orientation == Orientation.Horizontal ? point.X : point.Y;
        var delta = position - dragStartPosition;
        var firstSize = dragStartFirstSize + delta;
        var secondSize = dragStartSecondSize - delta;
        var total = firstSize + secondSize;
        if (!double.IsFinite(total) || total <= 0)
        {
            return;
        }

        ApplyDragCandidate(firstSize / total, secondSize / total, delta);
        args.Handled = true;
    }

    private void OnSplitHandlePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        args.Pointer.Capture(null);
        PublishCurrentState();
        args.Handled = true;
    }

    private void OnSplitHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        PublishCurrentState();
    }

    private void ApplyDragCandidate(double firstRatio, double secondRatio, double firstDelta)
    {
        if (!HasFirstPane || !HasSecondPane)
        {
            return;
        }

        var (normalizedFirst, normalizedSecond) = NormalizeRatios(firstRatio, secondRatio);
        var threshold = GetCollapseThresholdRatio();
        var firstCanCollapse = CanCollapseFirstPane && IsAtOrBelowCollapseThreshold(normalizedFirst, threshold);
        var secondCanCollapse = CanCollapseSecondPane && IsAtOrBelowCollapseThreshold(normalizedSecond, threshold);

        if (firstCanCollapse && secondCanCollapse)
        {
            firstCanCollapse = firstDelta <= 0;
            secondCanCollapse = firstDelta > 0;
        }

        if (firstCanCollapse)
        {
            ApplyPaneState(dragStartFirstRatio, dragStartSecondRatio, firstCollapsed: true, secondCollapsed: false);
            return;
        }

        if (secondCanCollapse)
        {
            ApplyPaneState(dragStartFirstRatio, dragStartSecondRatio, firstCollapsed: false, secondCollapsed: true);
            return;
        }

        ApplyPaneState(normalizedFirst, normalizedSecond, firstCollapsed: false, secondCollapsed: false);
    }

    private (double First, double Second) GetDefaultRatios()
    {
        if (DefaultFirstLength.IsStar && DefaultSecondLength.IsStar)
        {
            return NormalizeRatios(DefaultFirstLength.Value, DefaultSecondLength.Value);
        }

        var availableLength = GetAvailableLength();
        if (availableLength > 0)
        {
            if (DefaultFirstLength.IsAbsolute && DefaultSecondLength.IsAbsolute)
            {
                return NormalizeRatios(DefaultFirstLength.Value, DefaultSecondLength.Value);
            }

            if (DefaultFirstLength.IsAbsolute && DefaultSecondLength.IsStar)
            {
                var firstRatio = Math.Clamp(DefaultFirstLength.Value / availableLength, 0.01, 0.99);
                return (firstRatio, 1 - firstRatio);
            }

            if (DefaultFirstLength.IsStar && DefaultSecondLength.IsAbsolute)
            {
                var secondRatio = Math.Clamp(DefaultSecondLength.Value / availableLength, 0.01, 0.99);
                return (1 - secondRatio, secondRatio);
            }

            var firstLength = GetPaneLength(firstPaneRoot);
            var secondLength = GetPaneLength(secondPaneRoot);
            if (firstLength > 0 && secondLength > 0)
            {
                return NormalizeRatios(firstLength, secondLength);
            }
        }

        if (DefaultSecondLength.IsAbsolute)
        {
            return (0.75, 0.25);
        }

        if (DefaultFirstLength.IsAuto || DefaultSecondLength.IsAuto)
        {
            return (0.70, 0.30);
        }

        return (0.5, 0.5);
    }

    private static (double First, double Second) NormalizeRatios(double firstRatio, double secondRatio)
    {
        if (!double.IsFinite(firstRatio) || firstRatio <= 0)
        {
            firstRatio = 1;
        }

        if (!double.IsFinite(secondRatio) || secondRatio <= 0)
        {
            secondRatio = 1;
        }

        var total = firstRatio + secondRatio;
        if (!double.IsFinite(total) || total <= 0)
        {
            return (0.5, 0.5);
        }

        return (firstRatio / total, secondRatio / total);
    }

    private double GetAvailableLength()
    {
        var length = Orientation == Orientation.Horizontal ? Bounds.Width : Bounds.Height;
        return double.IsFinite(length) && length > SplitHandleThickness
            ? length - SplitHandleThickness
            : 0;
    }

    private double GetPaneLength(Control pane)
    {
        var length = Orientation == Orientation.Horizontal ? pane.Bounds.Width : pane.Bounds.Height;
        return double.IsFinite(length) && length > 0 ? length : 0;
    }

    private double GetCollapseThresholdRatio()
    {
        var threshold = CollapseThresholdRatio;
        return double.IsFinite(threshold)
            ? Math.Clamp(threshold, 0.01, 0.99)
            : DefaultCollapseThresholdRatio;
    }

    private double GetCollapsedHeaderThickness()
    {
        var thickness = CollapsedHeaderThickness;
        return double.IsFinite(thickness) && thickness > 0 ? thickness : DefaultCollapsedHeaderThickness;
    }

    private static bool IsAtOrBelowCollapseThreshold(double ratio, double threshold) =>
        ratio <= threshold + 1e-6;
}
