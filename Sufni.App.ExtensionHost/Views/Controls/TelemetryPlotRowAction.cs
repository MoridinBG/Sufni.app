using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.Views.Controls;

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
    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private TelemetryPlotRowActionKind kind;
    [ObservableProperty] private Geometry? iconGeometry;
    [ObservableProperty] private object? toolTip;
    [ObservableProperty] private ICommand? command;
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool isEnabled = true;
    [ObservableProperty] private bool isChecked;
    [ObservableProperty] private bool isHighlighted;
    [ObservableProperty] private TelemetryPlotRowActionTone tone;
}
