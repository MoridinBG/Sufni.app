using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Sessions.Analysis.Views.Controls;

public partial class BalanceAnalysisHost : AnalysisHostBase
{
    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<BalanceAnalysisHost, BalanceType>(nameof(BalanceType));

    public static readonly StyledProperty<BalanceDisplacementMode> BalanceDisplacementModeProperty =
        AvaloniaProperty.Register<BalanceAnalysisHost, BalanceDisplacementMode>(nameof(BalanceDisplacementMode));

    public static readonly StyledProperty<BalanceSpeedMode> BalanceSpeedModeProperty =
        AvaloniaProperty.Register<BalanceAnalysisHost, BalanceSpeedMode>(nameof(BalanceSpeedMode));

    public static readonly StyledProperty<string?> StaticSourceNameProperty =
        AvaloniaProperty.Register<BalanceAnalysisHost, string?>(nameof(StaticSourceName));

    public static readonly StyledProperty<string?> PlotNameProperty =
        AvaloniaProperty.Register<BalanceAnalysisHost, string?>(nameof(PlotName));

    public BalanceType BalanceType
    {
        get => GetValue(BalanceTypeProperty);
        set => SetValue(BalanceTypeProperty, value);
    }

    public BalanceDisplacementMode BalanceDisplacementMode
    {
        get => GetValue(BalanceDisplacementModeProperty);
        set => SetValue(BalanceDisplacementModeProperty, value);
    }

    public BalanceSpeedMode BalanceSpeedMode
    {
        get => GetValue(BalanceSpeedModeProperty);
        set => SetValue(BalanceSpeedModeProperty, value);
    }

    public string? StaticSourceName
    {
        get => GetValue(StaticSourceNameProperty);
        set => SetValue(StaticSourceNameProperty, value);
    }

    public string? PlotName
    {
        get => GetValue(PlotNameProperty);
        set => SetValue(PlotNameProperty, value);
    }

    public BalanceAnalysisHost()
    {
        InitializeComponent();
    }
}
