using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Controls;

public partial class RecordedSessionGraphRowsView : UserControl
{
    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<RecordedSessionGraphRowsView, bool>(nameof(HideRightAxis));

    public static readonly StyledProperty<int?> MaximumDisplayHzProperty =
        AvaloniaProperty.Register<RecordedSessionGraphRowsView, int?>(nameof(MaximumDisplayHz));

    public bool HideRightAxis
    {
        get => GetValue(HideRightAxisProperty);
        set => SetValue(HideRightAxisProperty, value);
    }

    public int? MaximumDisplayHz
    {
        get => GetValue(MaximumDisplayHzProperty);
        set => SetValue(MaximumDisplayHzProperty, value);
    }

    public RecordedSessionGraphRowsView()
    {
        InitializeComponent();
    }
}
