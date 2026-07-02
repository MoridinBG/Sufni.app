using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Sessions.Signals.Views.Controls;

public partial class RecordedSignalRowsView : UserControl
{
    public static readonly StyledProperty<bool> HideRightAxisProperty =
        AvaloniaProperty.Register<RecordedSignalRowsView, bool>(nameof(HideRightAxis));

    public static readonly StyledProperty<int?> MaximumDisplayHzProperty =
        AvaloniaProperty.Register<RecordedSignalRowsView, int?>(nameof(MaximumDisplayHz));

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

    public RecordedSignalRowsView()
    {
        InitializeComponent();
    }
}
