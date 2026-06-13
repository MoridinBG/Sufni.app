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

    public static readonly StyledProperty<GridLength> TravelHistogramRowHeightProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, GridLength>(
            nameof(TravelHistogramRowHeight),
            new GridLength(320));

    public static readonly StyledProperty<GridLength> TravelFrequencyHistogramRowHeightProperty =
        AvaloniaProperty.Register<TravelStatisticsHost, GridLength>(
            nameof(TravelFrequencyHistogramRowHeight),
            new GridLength(240));

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

    public GridLength TravelHistogramRowHeight
    {
        get => GetValue(TravelHistogramRowHeightProperty);
        set => SetValue(TravelHistogramRowHeightProperty, value);
    }

    public GridLength TravelFrequencyHistogramRowHeight
    {
        get => GetValue(TravelFrequencyHistogramRowHeightProperty);
        set => SetValue(TravelFrequencyHistogramRowHeightProperty, value);
    }

    public TravelStatisticsHost()
    {
        InitializeComponent();
    }
}
