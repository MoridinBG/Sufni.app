using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Views.Plots;

namespace Sufni.App.Views.Controls;

public sealed class TelemetryPlotRow : UserControl
{
    private static readonly IBrush defaultHeaderBackground = new SolidColorBrush(Color.Parse("#1a1f23"));
    private static readonly Color defaultBasePlotFigureBackground = Color.Parse("#15191C");
    private static readonly Color defaultBasePlotDataBackground = Color.Parse("#20262B");
    private static readonly Color defaultHostedPlotFigureBackground = Color.Parse("#171D21");
    private static readonly Color defaultHostedPlotDataBackground = Color.Parse("#222A30");
    private readonly Button headerButton;
    private readonly TextBlock chevronText;
    private readonly TextBlock titleText;
    private readonly Grid expandedGrid;
    private readonly PlaceholderOverlayContainer plotHost;
    private readonly ContentControl plotContentHost;
    private readonly ContentControl placeholderContentHost;
    private readonly StackPanel childRowsHost;

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
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(MinimumPlotHeight), defaultValue: 96);

    public static readonly StyledProperty<double> ChildRowGapProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, double>(nameof(ChildRowGap), defaultValue: 4);

    public static readonly StyledProperty<IBrush?> RowBackgroundProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, IBrush?>(nameof(RowBackground));

    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty =
        AvaloniaProperty.Register<TelemetryPlotRow, IBrush?>(nameof(HeaderBackground));

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

    public TelemetryPlotRow()
    {
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
        headerButton.Click += (_, _) => IsExpanded = !IsExpanded;

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

        Content = new Border
        {
            Background = RowBackground,
            Child = new Grid
            {
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

    private void ApplyHostedRowDefaults()
    {
        if (!IsSet(PlotFigureBackgroundProperty))
        {
            PlotFigureBackground = defaultHostedPlotFigureBackground;
        }

        if (!IsSet(PlotDataBackgroundProperty))
        {
            PlotDataBackground = defaultHostedPlotDataBackground;
        }
    }

    private void UpdateVisualState()
    {
        titleText.Text = Title;
        chevronText.Text = IsExpanded ? "v" : ">";
        headerButton.Height = IsExpanded ? HeaderHeight : CollapsedHeaderHeight;
        headerButton.Background = HeaderBackground ?? defaultHeaderBackground;
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

        if (Content is Border border)
        {
            border.Background = RowBackground;
        }
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
