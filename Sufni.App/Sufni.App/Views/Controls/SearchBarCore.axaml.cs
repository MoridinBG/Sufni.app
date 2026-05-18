using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sufni.App.Views.Controls;

public partial class SearchBarCore : UserControl
{
    public static readonly StyledProperty<object?> SecondaryContentProperty =
        AvaloniaProperty.Register<SearchBarCore, object?>(nameof(SecondaryContent));

    public static readonly StyledProperty<bool> CloseButtonVisibleProperty =
        AvaloniaProperty.Register<SearchBarCore, bool>(nameof(CloseButtonVisible));

    public static readonly StyledProperty<IBrush?> SearchBoxBackgroundProperty =
        AvaloniaProperty.Register<SearchBarCore, IBrush?>(nameof(SearchBoxBackground));

    public static readonly StyledProperty<Thickness> SearchBoxBorderThicknessProperty =
        AvaloniaProperty.Register<SearchBarCore, Thickness>(nameof(SearchBoxBorderThickness), new Thickness(0));

    public static readonly StyledProperty<CornerRadius> SearchBoxCornerRadiusProperty =
        AvaloniaProperty.Register<SearchBarCore, CornerRadius>(nameof(SearchBoxCornerRadius), new CornerRadius(5));

    public static readonly StyledProperty<IBrush?> SyncIndicatorForegroundProperty =
        AvaloniaProperty.Register<SearchBarCore, IBrush?>(nameof(SyncIndicatorForeground));

    public static readonly StyledProperty<GridLength> SearchRowHeightProperty =
        AvaloniaProperty.Register<SearchBarCore, GridLength>(nameof(SearchRowHeight), new GridLength(39));

    public object? SecondaryContent
    {
        get => GetValue(SecondaryContentProperty);
        set => SetValue(SecondaryContentProperty, value);
    }

    public bool CloseButtonVisible
    {
        get => GetValue(CloseButtonVisibleProperty);
        set => SetValue(CloseButtonVisibleProperty, value);
    }

    public IBrush? SearchBoxBackground
    {
        get => GetValue(SearchBoxBackgroundProperty);
        set => SetValue(SearchBoxBackgroundProperty, value);
    }

    public Thickness SearchBoxBorderThickness
    {
        get => GetValue(SearchBoxBorderThicknessProperty);
        set => SetValue(SearchBoxBorderThicknessProperty, value);
    }

    public CornerRadius SearchBoxCornerRadius
    {
        get => GetValue(SearchBoxCornerRadiusProperty);
        set => SetValue(SearchBoxCornerRadiusProperty, value);
    }

    public IBrush? SyncIndicatorForeground
    {
        get => GetValue(SyncIndicatorForegroundProperty);
        set => SetValue(SyncIndicatorForegroundProperty, value);
    }

    public GridLength SearchRowHeight
    {
        get => GetValue(SearchRowHeightProperty);
        set => SetValue(SearchRowHeightProperty, value);
    }

    public SearchBarCore()
    {
        InitializeComponent();
    }
}
