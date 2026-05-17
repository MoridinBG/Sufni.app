using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Views.Plots;

namespace Sufni.App.Views.Controls;

public sealed class TelemetryPlotRow : UserControl
{
    private const double HostedRowTitleLeftInset = 12;
    private const double HeaderClickMovementThresholdPixels = 6;
    private static readonly IBrush defaultHeaderBackground = new SolidColorBrush(Color.Parse("#1a1f23"));
    private static readonly IBrush dragHeaderBackground = new SolidColorBrush(Color.Parse("#263238"));
    private static readonly IBrush dropTargetHeaderBackground = new SolidColorBrush(Color.Parse("#1F3A46"));
    private static readonly IBrush defaultHostedRowBackground = new SolidColorBrush(Color.Parse("rgb(15, 19, 20)"));
    private static readonly IBrush defaultHostedHeaderBackground = new SolidColorBrush(Color.Parse("#101416"));
    private static readonly Color defaultBasePlotFigureBackground = Color.Parse("#15191C");
    private static readonly Color defaultBasePlotDataBackground = Color.Parse("#20262B");
    private static readonly Color defaultHostedPlotFigureBackground = Color.Parse("#101518");
    private static readonly Color defaultHostedPlotDataBackground = Color.Parse("#1A2024");
    private readonly Border rowBorder;
    private readonly Button headerButton;
    private readonly TextBlock chevronText;
    private readonly TextBlock titleText;
    private readonly Grid expandedGrid;
    private readonly PlaceholderOverlayContainer plotHost;
    private readonly ContentControl plotContentHost;
    private readonly ContentControl placeholderContentHost;
    private readonly StackPanel childRowsHost;
    private bool isHeaderClickCandidate;
    private bool isHeaderPointerActive;
    private bool isHeaderDragInProgress;
    private bool isDragFeedbackVisible;
    private bool isDropTargetFeedbackVisible;
    private Point headerClickStartPoint;

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, string?>(nameof(Title));

    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, SurfacePresentationState>(
            nameof(PresentationState),
            defaultValue: SurfacePresentationState.Ready);

    public static readonly StyledProperty<object?> PlotContentProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, object?>(nameof(PlotContent));

    public static readonly StyledProperty<object?> PlaceholderContentProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, object?>(nameof(PlaceholderContent));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, bool>(nameof(IsExpanded), defaultValue: true);

    public static readonly StyledProperty<double> HeaderHeightProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(HeaderHeight), defaultValue: 32);

    public static readonly StyledProperty<double> CollapsedHeaderHeightProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(CollapsedHeaderHeight), defaultValue: 32);

    public static readonly StyledProperty<double> PreferredPlotHeightProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(PreferredPlotHeight), defaultValue: 180);

    public static readonly StyledProperty<double> MinimumPlotHeightProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(MinimumPlotHeight), defaultValue: 160);

    public static readonly StyledProperty<double> ChildRowGapProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(ChildRowGap), defaultValue: 4);

    public static readonly StyledProperty<IBrush?> RowBackgroundProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, IBrush?>(nameof(RowBackground));

    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, IBrush?>(nameof(HeaderBackground));

    public static readonly StyledProperty<double> TitleLeftInsetProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(TitleLeftInset));

    public static readonly StyledProperty<Color> PlotFigureBackgroundProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, Color>(
            nameof(PlotFigureBackground),
            defaultValue: defaultBasePlotFigureBackground);

    public static readonly StyledProperty<Color> PlotDataBackgroundProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, Color>(
            nameof(PlotDataBackground),
            defaultValue: defaultBasePlotDataBackground);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public SurfacePresentationState PresentationState
    {
        get => GetValue(PresentationStateProperty);
        set => SetValue(PresentationStateProperty, value);
    }

    public object? PlotContent
    {
        get => GetValue(PlotContentProperty);
        set => SetValue(PlotContentProperty, value);
    }

    public object? PlaceholderContent
    {
        get => GetValue(PlaceholderContentProperty);
        set => SetValue(PlaceholderContentProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public double HeaderHeight
    {
        get => GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public double CollapsedHeaderHeight
    {
        get => GetValue(CollapsedHeaderHeightProperty);
        set => SetValue(CollapsedHeaderHeightProperty, value);
    }

    public double PreferredPlotHeight
    {
        get => GetValue(PreferredPlotHeightProperty);
        set => SetValue(PreferredPlotHeightProperty, value);
    }

    public double MinimumPlotHeight
    {
        get => GetValue(MinimumPlotHeightProperty);
        set => SetValue(MinimumPlotHeightProperty, value);
    }

    public double ChildRowGap
    {
        get => GetValue(ChildRowGapProperty);
        set => SetValue(ChildRowGapProperty, value);
    }

    public IBrush? RowBackground
    {
        get => GetValue(RowBackgroundProperty);
        set => SetValue(RowBackgroundProperty, value);
    }

    public IBrush? HeaderBackground
    {
        get => GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    public double TitleLeftInset
    {
        get => GetValue(TitleLeftInsetProperty);
        set => SetValue(TitleLeftInsetProperty, value);
    }

    public Color PlotFigureBackground
    {
        get => GetValue(PlotFigureBackgroundProperty);
        set => SetValue(PlotFigureBackgroundProperty, value);
    }

    public Color PlotDataBackground
    {
        get => GetValue(PlotDataBackgroundProperty);
        set => SetValue(PlotDataBackgroundProperty, value);
    }

    public AvaloniaList<TelemetryPlotRow> ChildRows { get; } = [];

    internal double AllocatedGroupHeight { get; private set; }
    internal double AllocatedPlotHeight { get; private set; }
    internal double? ManualGroupHeight { get; set; }
    internal bool ReservesLayout => PresentationState.ReservesLayout || ChildRows.Any(row => row.ReservesLayout);
    internal bool IsDragFeedbackVisible => isDragFeedbackVisible;
    internal bool IsDropTargetFeedbackVisible => isDropTargetFeedbackVisible;

    public TelemetryPlotRow()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ClipToBounds = true;

        chevronText = new TextBlock
        {
            Width = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titleText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
        };

        headerButton = new Button
        {
            Padding = new Thickness(8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = defaultHeaderBackground,
            BorderThickness = new Thickness(0),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
                Children =
                {
                    chevronText,
                    titleText,
                },
            },
        };
        Grid.SetColumn(titleText, 1);
        headerButton.AddHandler<PointerPressedEventArgs>(
            InputElement.PointerPressedEvent,
            OnHeaderPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        headerButton.PointerMoved += OnHeaderPointerMoved;
        headerButton.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            OnHeaderPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        headerButton.PointerCaptureLost += (_, _) => OnHeaderPointerCaptureLost();

        plotContentHost = new ContentControl();
        placeholderContentHost = new ContentControl();
        plotHost = new PlaceholderOverlayContainer
        {
            ReadyContent = plotContentHost,
            PlaceholderContent = placeholderContentHost,
        };
        childRowsHost = new StackPanel();
        expandedGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                plotHost,
                childRowsHost,
            },
        };
        Grid.SetRow(childRowsHost, 1);

        rowBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = RowBackground,
            Child = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                },
                Children =
                {
                    headerButton,
                    expandedGrid,
                },
            },
        };
        Content = rowBorder;
        Grid.SetRow(expandedGrid, 1);

        ChildRows.CollectionChanged += OnChildRowsChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == TitleProperty ||
                e.Property == PresentationStateProperty ||
                e.Property == PlotContentProperty ||
                e.Property == PlaceholderContentProperty ||
                e.Property == IsExpandedProperty ||
                e.Property == HeaderHeightProperty ||
                e.Property == CollapsedHeaderHeightProperty ||
                e.Property == PreferredPlotHeightProperty ||
                e.Property == MinimumPlotHeightProperty ||
                e.Property == ChildRowGapProperty ||
                e.Property == RowBackgroundProperty ||
                e.Property == HeaderBackgroundProperty ||
                e.Property == TitleLeftInsetProperty ||
                e.Property == PlotFigureBackgroundProperty ||
                e.Property == PlotDataBackgroundProperty)
            {
                UpdateVisualState();
                InvalidateRowLayout();
            }
        };
        UpdateVisualState();
    }

    internal double GetPreferredGroupHeight()
    {
        return GetGroupHeight(PreferredPlotHeight);
    }

    internal double GetMinimumGroupHeight()
    {
        return GetGroupHeight(MinimumPlotHeight);
    }

    internal int GetVisiblePlotSlotCount()
    {
        if (!ReservesLayout || !IsExpanded)
        {
            return 0;
        }

        return (HasOwnPlotSlot ? 1 : 0) +
               ChildRows
                   .Where(row => row.ReservesLayout)
                   .Sum(row => row.GetVisiblePlotSlotCount());
    }

    internal double GetFixedHeightExcludingPlots()
    {
        if (!ReservesLayout)
        {
            return 0;
        }

        if (!IsExpanded)
        {
            return CollapsedHeaderHeight;
        }

        var childRows = ChildRows.Where(row => row.ReservesLayout).ToArray();
        return HeaderHeight +
               Math.Max(0, childRows.Length - 1) * ChildRowGap +
               childRows.Sum(row => row.GetFixedHeightExcludingPlots());
    }

    internal double ApplyAllocatedGroupHeight(double groupHeight)
    {
        if (!ReservesLayout)
        {
            AllocatedGroupHeight = 0;
            AllocatedPlotHeight = 0;
            UpdateVisualState();
            return 0;
        }

        if (!IsExpanded)
        {
            AllocatedGroupHeight = CollapsedHeaderHeight;
            AllocatedPlotHeight = 0;
            UpdateVisualState();
            return AllocatedGroupHeight;
        }

        var slotCount = GetVisiblePlotSlotCount();
        var fixedHeight = GetFixedHeightExcludingPlots();
        var plotHeight = slotCount > 0
            ? Math.Max(MinimumPlotHeight, (groupHeight - fixedHeight) / slotCount)
            : 0;

        ApplyPlotHeight(plotHeight);
        return AllocatedGroupHeight;
    }

    private bool HasOwnPlotSlot => PlotContent is not null && PresentationState.ReservesLayout;

    private double GetGroupHeight(double plotHeight)
    {
        if (!ReservesLayout)
        {
            return 0;
        }

        if (!IsExpanded)
        {
            return CollapsedHeaderHeight;
        }

        return GetFixedHeightExcludingPlots() + GetVisiblePlotSlotCount() * plotHeight;
    }

    private double ApplyPlotHeight(double plotHeight)
    {
        if (!ReservesLayout)
        {
            AllocatedGroupHeight = 0;
            AllocatedPlotHeight = 0;
            return 0;
        }

        if (!IsExpanded)
        {
            AllocatedGroupHeight = CollapsedHeaderHeight;
            AllocatedPlotHeight = 0;
            return AllocatedGroupHeight;
        }

        AllocatedPlotHeight = HasOwnPlotSlot ? plotHeight : 0;
        var height = HeaderHeight + AllocatedPlotHeight;
        var visibleChildren = ChildRows.Where(row => row.ReservesLayout).ToArray();
        if (visibleChildren.Length > 0)
        {
            height += Math.Max(0, visibleChildren.Length - 1) * ChildRowGap;
            height += visibleChildren.Sum(row => row.ApplyPlotHeight(plotHeight));
        }

        AllocatedGroupHeight = height;
        UpdateVisualState();
        return AllocatedGroupHeight;
    }

    private void OnChildRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var oldRow in e.OldItems.OfType<TelemetryPlotRow>())
            {
                oldRow.PropertyChanged -= OnChildRowPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var newRow in e.NewItems.OfType<TelemetryPlotRow>())
            {
                newRow.PropertyChanged += OnChildRowPropertyChanged;
            }
        }

        RebuildChildRows();
        InvalidateRowLayout();
    }

    private void OnChildRowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == PresentationStateProperty ||
            e.Property == IsExpandedProperty ||
            e.Property == PlotContentProperty ||
            e.Property == HeaderHeightProperty ||
            e.Property == CollapsedHeaderHeightProperty ||
            e.Property == PreferredPlotHeightProperty ||
            e.Property == MinimumPlotHeightProperty ||
            e.Property == ChildRowGapProperty)
        {
            InvalidateRowLayout();
        }
    }

    private void RebuildChildRows()
    {
        childRowsHost.Children.Clear();
        foreach (var row in ChildRows)
        {
            row.ApplyHostedRowDefaults();
            childRowsHost.Children.Add(row);
        }
    }

    internal void ApplyHostedRowDefaults()
    {
        if (!IsSet(RowBackgroundProperty))
        {
            RowBackground = defaultHostedRowBackground;
        }

        if (!IsSet(HeaderBackgroundProperty))
        {
            HeaderBackground = defaultHostedHeaderBackground;
        }

        if (!IsSet(TitleLeftInsetProperty))
        {
            TitleLeftInset = HostedRowTitleLeftInset;
        }

        if (!IsSet(PlotFigureBackgroundProperty))
        {
            PlotFigureBackground = defaultHostedPlotFigureBackground;
        }

        if (!IsSet(PlotDataBackgroundProperty))
        {
            PlotDataBackground = defaultHostedPlotDataBackground;
        }
    }

    internal void ApplyRootRowDefaults()
    {
        if (ReferenceEquals(RowBackground, defaultHostedRowBackground))
        {
            ClearValue(RowBackgroundProperty);
        }

        if (ReferenceEquals(HeaderBackground, defaultHostedHeaderBackground))
        {
            ClearValue(HeaderBackgroundProperty);
        }

        if (TitleLeftInset == HostedRowTitleLeftInset)
        {
            ClearValue(TitleLeftInsetProperty);
        }

        if (PlotFigureBackground == defaultHostedPlotFigureBackground)
        {
            ClearValue(PlotFigureBackgroundProperty);
        }

        if (PlotDataBackground == defaultHostedPlotDataBackground)
        {
            ClearValue(PlotDataBackgroundProperty);
        }

        UpdateVisualState();
    }

    internal bool HasDescendant(TelemetryPlotRow candidate)
    {
        return ChildRows.Any(row => ReferenceEquals(row, candidate) || row.HasDescendant(candidate));
    }

    internal bool HeaderContainsPoint(Point point, Control relativeTo)
    {
        var origin = headerButton.TranslatePoint(new Point(0, 0), relativeTo);
        if (origin is null)
        {
            return false;
        }

        return new Rect(origin.Value, headerButton.Bounds.Size).Contains(point);
    }

    internal void SetDragFeedback(bool isVisible)
    {
        if (isDragFeedbackVisible == isVisible)
        {
            return;
        }

        isDragFeedbackVisible = isVisible;
        UpdateVisualState();
    }

    internal void SetDropTargetFeedback(bool isVisible)
    {
        if (isDropTargetFeedbackVisible == isVisible)
        {
            return;
        }

        isDropTargetFeedbackVisible = isVisible;
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        titleText.Text = Title;
        chevronText.Text = IsExpanded ? "-" : "+";
        chevronText.Margin = new Thickness(TitleLeftInset, 0, 0, 0);
        headerButton.Height = IsExpanded ? HeaderHeight : CollapsedHeaderHeight;
        headerButton.Background = isDropTargetFeedbackVisible
            ? dropTargetHeaderBackground
            : isDragFeedbackVisible
                ? dragHeaderBackground
                : HeaderBackground ?? defaultHeaderBackground;
        expandedGrid.IsVisible = IsExpanded && ReservesLayout;
        plotHost.PresentationState = PresentationState;
        plotHost.Height = HasOwnPlotSlot ? AllocatedPlotHeight : 0;
        plotHost.IsVisible = HasOwnPlotSlot && IsExpanded;
        plotContentHost.Content = PlotContent;
        placeholderContentHost.Content = PlaceholderContent;
        ApplyPlotBackgrounds(PlotContent);
        childRowsHost.Spacing = ChildRowGap;
        childRowsHost.IsVisible = IsExpanded && ChildRows.Any(row => row.ReservesLayout);
        IsVisible = ReservesLayout;
        Opacity = isDragFeedbackVisible ? 0.72 : 1;
        rowBorder.Background = RowBackground;
        rowBorder.Margin = new Thickness(0);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!IsPrimaryPointerPressed(args))
        {
            return;
        }

        isHeaderClickCandidate = true;
        isHeaderPointerActive = true;
        isHeaderDragInProgress = false;
        headerClickStartPoint = args.GetPosition(headerButton);
        args.Pointer.Capture(headerButton);
        args.Handled = true;
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs args)
    {
        if (!isHeaderPointerActive)
        {
            return;
        }

        if (!isHeaderDragInProgress && HasExceededHeaderClickMovement(args))
        {
            isHeaderClickCandidate = false;
            isHeaderDragInProgress = true;
            this.FindAncestorOfType<TelemetryPlotsRoot>()?.BeginRowDragFeedback(this);
        }

        if (isHeaderDragInProgress)
        {
            this.FindAncestorOfType<TelemetryPlotsRoot>()?.UpdateRowDragFeedback(this, args);
            args.Handled = true;
        }
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!isHeaderPointerActive)
        {
            return;
        }

        if (isHeaderDragInProgress)
        {
            var root = this.FindAncestorOfType<TelemetryPlotsRoot>();
            root?.TryDropDraggedRow(this, args);
            root?.EndRowDragFeedback(this);
        }
        else if (isHeaderClickCandidate &&
                 IsWithinHeaderBounds(args.GetPosition(headerButton)) &&
                 !HasExceededHeaderClickMovement(args))
        {
            IsExpanded = !IsExpanded;
        }

        ResetHeaderPointerState();
        args.Pointer.Capture(null);
        args.Handled = true;
    }

    private void ResetHeaderPointerState()
    {
        isHeaderClickCandidate = false;
        isHeaderPointerActive = false;
        isHeaderDragInProgress = false;
    }

    private void OnHeaderPointerCaptureLost()
    {
        if (isHeaderDragInProgress)
        {
            this.FindAncestorOfType<TelemetryPlotsRoot>()?.EndRowDragFeedback(this);
        }

        ResetHeaderPointerState();
    }

    private bool HasExceededHeaderClickMovement(PointerEventArgs args)
    {
        var point = args.GetPosition(headerButton);
        var delta = point - headerClickStartPoint;
        return Math.Abs(delta.X) > HeaderClickMovementThresholdPixels ||
               Math.Abs(delta.Y) > HeaderClickMovementThresholdPixels;
    }

    private bool IsPrimaryPointerPressed(PointerEventArgs args)
    {
        var point = args.GetCurrentPoint(headerButton);
        return point.Properties.IsLeftButtonPressed || args.Pointer.Type != PointerType.Mouse;
    }

    private bool IsWithinHeaderBounds(Point point)
    {
        return point.X >= 0 &&
               point.Y >= 0 &&
               point.X <= headerButton.Bounds.Width &&
               point.Y <= headerButton.Bounds.Height;
    }

    private void ApplyPlotBackgrounds(object? content)
    {
        if (content is not SufniPlotView plotView)
        {
            return;
        }

        plotView.PlotFigureBackground = PlotFigureBackground;
        plotView.PlotDataBackground = PlotDataBackground;
    }

    private void InvalidateRowLayout()
    {
        InvalidateMeasure();
        this.FindAncestorOfType<TelemetryPlotRow>()?.InvalidateRowLayout();
        this.FindAncestorOfType<TelemetryPlotRowsPanel>()?.InvalidateMeasure();
    }
}
