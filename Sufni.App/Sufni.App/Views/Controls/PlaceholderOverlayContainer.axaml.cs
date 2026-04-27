using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sufni.App.Presentation;

namespace Sufni.App.Views.Controls;

public partial class PlaceholderOverlayContainer : UserControl
{
    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<PlaceholderOverlayContainer, SurfacePresentationState>(
            nameof(PresentationState),
            defaultValue: SurfacePresentationState.Hidden);

    public static readonly StyledProperty<object?> ReadyContentProperty =
        AvaloniaProperty.Register<PlaceholderOverlayContainer, object?>(nameof(ReadyContent));

    public static readonly StyledProperty<object?> PlaceholderContentProperty =
        AvaloniaProperty.Register<PlaceholderOverlayContainer, object?>(nameof(PlaceholderContent));

    public static readonly StyledProperty<IBrush?> OverlayTintBrushProperty =
        AvaloniaProperty.Register<PlaceholderOverlayContainer, IBrush?>(
            nameof(OverlayTintBrush),
            defaultValue: new SolidColorBrush(Color.Parse("#0d0d0d")));

    public static readonly StyledProperty<double> OverlayTintOpacityProperty =
        AvaloniaProperty.Register<PlaceholderOverlayContainer, double>(nameof(OverlayTintOpacity), defaultValue: 0.65);

    public SurfacePresentationState PresentationState
    {
        get => GetValue(PresentationStateProperty);
        set => SetValue(PresentationStateProperty, value);
    }

    public object? ReadyContent
    {
        get => GetValue(ReadyContentProperty);
        set => SetValue(ReadyContentProperty, value);
    }

    public object? PlaceholderContent
    {
        get => GetValue(PlaceholderContentProperty);
        set => SetValue(PlaceholderContentProperty, value);
    }

    public IBrush? OverlayTintBrush
    {
        get => GetValue(OverlayTintBrushProperty);
        set => SetValue(OverlayTintBrushProperty, value);
    }

    public double OverlayTintOpacity
    {
        get => GetValue(OverlayTintOpacityProperty);
        set => SetValue(OverlayTintOpacityProperty, value);
    }

    public PlaceholderOverlayContainer()
    {
        InitializeComponent();
    }
}