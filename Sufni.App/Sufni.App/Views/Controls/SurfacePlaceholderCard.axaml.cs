using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sufni.App.Views.Controls;

public partial class SurfacePlaceholderCard : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SurfacePlaceholderCard, string?>(nameof(Title));

    public static readonly StyledProperty<IBrush?> PreviewBrushProperty =
        AvaloniaProperty.Register<SurfacePlaceholderCard, IBrush?>(
            nameof(PreviewBrush),
            defaultValue: new SolidColorBrush(Color.Parse("#2c3946")));

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<SurfacePlaceholderCard, double>(nameof(MinCardHeight));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IBrush? PreviewBrush
    {
        get => GetValue(PreviewBrushProperty);
        set => SetValue(PreviewBrushProperty, value);
    }

    public double MinCardHeight
    {
        get => GetValue(MinCardHeightProperty);
        set => SetValue(MinCardHeightProperty, value);
    }

    public SurfacePlaceholderCard()
    {
        InitializeComponent();
    }
}