using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.LiveDaq.Views.Shared;

public partial class LiveDaqDeviceManagementCard : UserControl
{
    public static readonly StyledProperty<Thickness> CardPaddingProperty =
        AvaloniaProperty.Register<LiveDaqDeviceManagementCard, Thickness>(nameof(CardPadding), new Thickness(12));

    public Thickness CardPadding
    {
        get => GetValue(CardPaddingProperty);
        set => SetValue(CardPaddingProperty, value);
    }

    public LiveDaqDeviceManagementCard()
    {
        InitializeComponent();
    }
}
