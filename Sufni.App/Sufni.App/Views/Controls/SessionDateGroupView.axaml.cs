using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Sufni.App.Views.Controls;

public partial class SessionDateGroupView : UserControl
{
    public static readonly StyledProperty<IDataTemplate?> RowTemplateProperty =
        AvaloniaProperty.Register<SessionDateGroupView, IDataTemplate?>(nameof(RowTemplate));

    public IDataTemplate? RowTemplate
    {
        get => GetValue(RowTemplateProperty);
        set => SetValue(RowTemplateProperty, value);
    }

    public static readonly StyledProperty<Thickness> HeaderPaddingProperty =
        AvaloniaProperty.Register<SessionDateGroupView, Thickness>(nameof(HeaderPadding), new Thickness(14, 7));

    public Thickness HeaderPadding
    {
        get => GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }

    public static readonly StyledProperty<double> HeaderMinHeightProperty =
        AvaloniaProperty.Register<SessionDateGroupView, double>(nameof(HeaderMinHeight), 40);

    public double HeaderMinHeight
    {
        get => GetValue(HeaderMinHeightProperty);
        set => SetValue(HeaderMinHeightProperty, value);
    }

    public SessionDateGroupView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowTemplateProperty)
        {
            DateGroupItemsRepeater.ItemTemplate = RowTemplate;
        }
    }
}
