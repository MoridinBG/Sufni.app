using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Shared;

public partial class LiveSessionHeaderFields : UserControl
{
    public static readonly StyledProperty<double> FieldFontSizeProperty =
        AvaloniaProperty.Register<LiveSessionHeaderFields, double>(nameof(FieldFontSize), 12);

    public static readonly StyledProperty<double> FieldOpacityProperty =
        AvaloniaProperty.Register<LiveSessionHeaderFields, double>(nameof(FieldOpacity), 0.85);

    public static readonly StyledProperty<double> FieldSpacingProperty =
        AvaloniaProperty.Register<LiveSessionHeaderFields, double>(nameof(FieldSpacing), 2);

    public static readonly StyledProperty<double> WaitingOpacityProperty =
        AvaloniaProperty.Register<LiveSessionHeaderFields, double>(nameof(WaitingOpacity), 0.7);

    public double FieldFontSize
    {
        get => GetValue(FieldFontSizeProperty);
        set => SetValue(FieldFontSizeProperty, value);
    }

    public double FieldOpacity
    {
        get => GetValue(FieldOpacityProperty);
        set => SetValue(FieldOpacityProperty, value);
    }

    public double FieldSpacing
    {
        get => GetValue(FieldSpacingProperty);
        set => SetValue(FieldSpacingProperty, value);
    }

    public double WaitingOpacity
    {
        get => GetValue(WaitingOpacityProperty);
        set => SetValue(WaitingOpacityProperty, value);
    }

    public LiveSessionHeaderFields()
    {
        InitializeComponent();
    }
}
