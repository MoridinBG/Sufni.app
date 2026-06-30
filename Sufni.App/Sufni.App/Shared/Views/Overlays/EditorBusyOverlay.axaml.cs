using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Shared.Views.Overlays;

public partial class EditorBusyOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<EditorBusyOverlay, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public EditorBusyOverlay()
    {
        InitializeComponent();
    }
}
