using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Views.Controls;

public partial class StrokeAnalysisHost : AnalysisHostBase
{
    public static readonly StyledProperty<GridLength> CompressionLengthRowHeightProperty =
        AvaloniaProperty.Register<StrokeAnalysisHost, GridLength>(
            nameof(CompressionLengthRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> CompressionSpeedRowHeightProperty =
        AvaloniaProperty.Register<StrokeAnalysisHost, GridLength>(
            nameof(CompressionSpeedRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> ReboundLengthRowHeightProperty =
        AvaloniaProperty.Register<StrokeAnalysisHost, GridLength>(
            nameof(ReboundLengthRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> ReboundSpeedRowHeightProperty =
        AvaloniaProperty.Register<StrokeAnalysisHost, GridLength>(
            nameof(ReboundSpeedRowHeight),
            new GridLength(220));

    public static readonly StyledProperty<GridLength> DeepTravelRowHeightProperty =
        AvaloniaProperty.Register<StrokeAnalysisHost, GridLength>(
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

    public StrokeAnalysisHost()
    {
        InitializeComponent();
    }
}
