using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sufni.App.Views.Controls;

public partial class BusyOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(IsActive));

    public static readonly StyledProperty<bool> ShowTintProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(ShowTint));

    public static readonly StyledProperty<bool> ConsumesInputProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(ConsumesInput));

    public static readonly StyledProperty<bool> UseStackLayoutProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(UseStackLayout));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<BusyOverlay, string?>(nameof(Message));

    public static readonly StyledProperty<string?> SecondaryMessageProperty =
        AvaloniaProperty.Register<BusyOverlay, string?>(nameof(SecondaryMessage));

    public static readonly StyledProperty<bool> ShowMessageProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(ShowMessage), defaultValue: true);

    public static readonly StyledProperty<bool> ShowSecondaryMessageProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(ShowSecondaryMessage));

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(ShowProgress));

    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<BusyOverlay, double>(nameof(ProgressValue));

    public static readonly StyledProperty<bool> IsProgressIndeterminateProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(IsProgressIndeterminate));

    public static readonly StyledProperty<IBrush?> TintBackgroundProperty =
        AvaloniaProperty.Register<BusyOverlay, IBrush?>(nameof(TintBackground));

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<BusyOverlay, double>(nameof(TintOpacity), defaultValue: 0.65);

    public static readonly StyledProperty<double> IndicatorSizeProperty =
        AvaloniaProperty.Register<BusyOverlay, double>(nameof(IndicatorSize), defaultValue: 40);

    public static readonly StyledProperty<IBrush?> IndicatorForegroundProperty =
        AvaloniaProperty.Register<BusyOverlay, IBrush?>(nameof(IndicatorForeground));

    public static readonly StyledProperty<IBrush?> MessageForegroundProperty =
        AvaloniaProperty.Register<BusyOverlay, IBrush?>(nameof(MessageForeground));

    public static readonly StyledProperty<FontWeight> BusyMessageFontWeightProperty =
        AvaloniaProperty.Register<BusyOverlay, FontWeight>(nameof(BusyMessageFontWeight), defaultValue: FontWeight.Normal);

    public static readonly StyledProperty<Thickness> MessageMarginProperty =
        AvaloniaProperty.Register<BusyOverlay, Thickness>(nameof(MessageMargin), defaultValue: new Thickness());

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<BusyOverlay, double>(nameof(Spacing), defaultValue: 14);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool ShowTint
    {
        get => GetValue(ShowTintProperty);
        set => SetValue(ShowTintProperty, value);
    }

    public bool ConsumesInput
    {
        get => GetValue(ConsumesInputProperty);
        set => SetValue(ConsumesInputProperty, value);
    }

    public bool UseStackLayout
    {
        get => GetValue(UseStackLayoutProperty);
        set => SetValue(UseStackLayoutProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? SecondaryMessage
    {
        get => GetValue(SecondaryMessageProperty);
        set => SetValue(SecondaryMessageProperty, value);
    }

    public bool ShowMessage
    {
        get => GetValue(ShowMessageProperty);
        set => SetValue(ShowMessageProperty, value);
    }

    public bool ShowSecondaryMessage
    {
        get => GetValue(ShowSecondaryMessageProperty);
        set => SetValue(ShowSecondaryMessageProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public double ProgressValue
    {
        get => GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public bool IsProgressIndeterminate
    {
        get => GetValue(IsProgressIndeterminateProperty);
        set => SetValue(IsProgressIndeterminateProperty, value);
    }

    public IBrush? TintBackground
    {
        get => GetValue(TintBackgroundProperty);
        set => SetValue(TintBackgroundProperty, value);
    }

    public double TintOpacity
    {
        get => GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }

    public double IndicatorSize
    {
        get => GetValue(IndicatorSizeProperty);
        set => SetValue(IndicatorSizeProperty, value);
    }

    public IBrush? IndicatorForeground
    {
        get => GetValue(IndicatorForegroundProperty);
        set => SetValue(IndicatorForegroundProperty, value);
    }

    public IBrush? MessageForeground
    {
        get => GetValue(MessageForegroundProperty);
        set => SetValue(MessageForegroundProperty, value);
    }

    public FontWeight BusyMessageFontWeight
    {
        get => GetValue(BusyMessageFontWeightProperty);
        set => SetValue(BusyMessageFontWeightProperty, value);
    }

    public Thickness MessageMargin
    {
        get => GetValue(MessageMarginProperty);
        set => SetValue(MessageMarginProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public BusyOverlay()
    {
        InitializeComponent();
    }
}
