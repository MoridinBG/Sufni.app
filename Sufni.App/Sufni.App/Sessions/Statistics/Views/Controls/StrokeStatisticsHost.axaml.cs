using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Statistics.Views.Controls;

public partial class StrokeStatisticsHost : StatisticsHostBase
{
    public static readonly StyledProperty<GridLength> CompressionLengthRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(CompressionLengthRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> CompressionSpeedRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(CompressionSpeedRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> ReboundLengthRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(ReboundLengthRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> ReboundSpeedRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(ReboundSpeedRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> DeepTravelRowHeightProperty =
        AvaloniaProperty.Register<StrokeStatisticsHost, GridLength>(
            nameof(DeepTravelRowHeight),
            new GridLength(180));

    public GridLength CompressionLengthRowHeight
    {
        get => GetValue(CompressionLengthRowHeightProperty);
        set => SetValue(CompressionLengthRowHeightProperty, value);
    }

    public GridLength CompressionSpeedRowHeight
    {
        get => GetValue(CompressionSpeedRowHeightProperty);
        set => SetValue(CompressionSpeedRowHeightProperty, value);
    }

    public GridLength ReboundLengthRowHeight
    {
        get => GetValue(ReboundLengthRowHeightProperty);
        set => SetValue(ReboundLengthRowHeightProperty, value);
    }

    public GridLength ReboundSpeedRowHeight
    {
        get => GetValue(ReboundSpeedRowHeightProperty);
        set => SetValue(ReboundSpeedRowHeightProperty, value);
    }

    public GridLength DeepTravelRowHeight
    {
        get => GetValue(DeepTravelRowHeightProperty);
        set => SetValue(DeepTravelRowHeightProperty, value);
    }

    public StrokeStatisticsHost()
    {
        InitializeComponent();
    }
}
