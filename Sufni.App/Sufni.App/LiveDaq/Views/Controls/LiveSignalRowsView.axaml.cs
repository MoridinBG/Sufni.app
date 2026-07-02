using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.LiveDaq.Views.Controls;

public partial class LiveSignalRowsView : UserControl
{
    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<LiveSignalRowsView, bool>(nameof(HideRightAxis));

    public bool HideRightAxis
    {
        get => GetValue(HideRightAxisProperty);
        set => SetValue(HideRightAxisProperty, value);
    }

    public LiveSignalRowsView()
    {
        InitializeComponent();
    }
}
