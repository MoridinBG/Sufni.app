using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Views.Controls;

public partial class TravelAnalysisHost : AnalysisHostBase
{
    public static readonly StyledProperty<TravelDistributionMode> TravelDistributionModeProperty =
        AvaloniaProperty.Register<TravelAnalysisHost, TravelDistributionMode>(nameof(TravelDistributionMode));

    public static readonly StyledProperty<bool> ShowFrequencyDistributionProperty =
        AvaloniaProperty.Register<TravelAnalysisHost, bool>(nameof(ShowFrequencyDistribution));

    public static readonly StyledProperty<GridLength> TravelDistributionRowHeightProperty =
        AvaloniaProperty.Register<TravelAnalysisHost, GridLength>(
            nameof(TravelDistributionRowHeight),
            new GridLength(320));

    public static readonly StyledProperty<GridLength> TravelFrequencyDistributionRowHeightProperty =
        AvaloniaProperty.Register<TravelAnalysisHost, GridLength>(
            nameof(TravelFrequencyDistributionRowHeight),
            new GridLength(240));

    public TravelDistributionMode TravelDistributionMode
    {
        get => GetValue(TravelDistributionModeProperty);
        set => SetValue(TravelDistributionModeProperty, value);
    }

    public bool ShowFrequencyDistribution
    {
        get => GetValue(ShowFrequencyDistributionProperty);
        set => SetValue(ShowFrequencyDistributionProperty, value);
    }

    public GridLength TravelDistributionRowHeight
    {
        get => GetValue(TravelDistributionRowHeightProperty);
        set => SetValue(TravelDistributionRowHeightProperty, value);
    }

    public GridLength TravelFrequencyDistributionRowHeight
    {
        get => GetValue(TravelFrequencyDistributionRowHeightProperty);
        set => SetValue(TravelFrequencyDistributionRowHeightProperty, value);
    }

    public TravelAnalysisHost()
    {
        InitializeComponent();
    }
}
