using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Controls;

public partial class LiveSessionGraphRowsView : UserControl
{
    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<LiveSessionGraphRowsView, bool>(nameof(HideRightAxis));

    public bool HideRightAxis
    {
        get => GetValue(HideRightAxisProperty);
        set => SetValue(HideRightAxisProperty, value);
    }

    public LiveSessionGraphRowsView()
    {
        InitializeComponent();
    }
}
