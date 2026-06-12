using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public partial class TravelStatisticsHost : StatisticsHostBase
{
    public static readonly StyledProperty<TravelHistogramMode> TravelHistogramModeProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, TravelHistogramMode>(nameof(TravelHistogramMode));

    public static readonly StyledProperty<bool> ShowFrequencyHistogramProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, bool>(nameof(ShowFrequencyHistogram));

    public TravelHistogramMode TravelHistogramMode
    {
        get => GetValue(TravelHistogramModeProperty);
        set => SetValue(TravelHistogramModeProperty, value);
    }

    public bool ShowFrequencyHistogram
    {
        get => GetValue(ShowFrequencyHistogramProperty);
        set => SetValue(ShowFrequencyHistogramProperty, value);
    }

    public TravelStatisticsHost()
    {
        InitializeComponent();
    }
}
