using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.ExtensionHost.Runtime.Presentation;

public enum TelemetryPlotRowActionKind
{
    Toggle,
    Execute,
}

public enum TelemetryPlotRowActionTone
{
    Default,
    Accent,
    Danger,
}

public sealed partial class TelemetryPlotRowAction : ObservableObject
{
    [ObservableProperty] public partial string Id { get; set; } = string.Empty;
    [ObservableProperty] public partial TelemetryPlotRowActionKind Kind { get; set; }
    [ObservableProperty] public partial string? IconPathData { get; set; }
    [ObservableProperty] public partial object? ToolTip { get; set; }
    [ObservableProperty] public partial ICommand? Command { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsEnabled { get; set; } = true;
    [ObservableProperty] public partial bool IsChecked { get; set; }
    [ObservableProperty] public partial bool IsHighlighted { get; set; }
    [ObservableProperty] public partial TelemetryPlotRowActionTone Tone { get; set; }
}
