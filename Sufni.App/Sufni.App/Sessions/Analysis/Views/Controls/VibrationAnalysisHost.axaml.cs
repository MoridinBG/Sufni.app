using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Views.Controls;

public partial class VibrationAnalysisHost : AnalysisHostBase
{
    public static readonly StyledProperty<string?> HostNameProperty =
        AvaloniaProperty.Register<VibrationAnalysisHost, string?>(nameof(HostName));

    public static readonly StyledProperty<ImuLocation> ImuLocationProperty =
        AvaloniaProperty.Register<VibrationAnalysisHost, ImuLocation>(nameof(ImuLocation));

    public static readonly StyledProperty<GridLength> PlotRowHeightProperty =
        AvaloniaProperty.Register<VibrationAnalysisHost, GridLength>(
            nameof(PlotRowHeight),
            new GridLength(1, GridUnitType.Star));

    public string? HostName
    {
        get => GetValue(HostNameProperty);
        set => SetValue(HostNameProperty, value);
    }

    public ImuLocation ImuLocation
    {
        get => GetValue(ImuLocationProperty);
        set => SetValue(ImuLocationProperty, value);
    }

    public GridLength PlotRowHeight
    {
        get => GetValue(PlotRowHeightProperty);
        set => SetValue(PlotRowHeightProperty, value);
    }

    public VibrationAnalysisHost()
    {
        InitializeComponent();
    }
}
