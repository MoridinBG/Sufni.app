using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Views.Controls;

public partial class BalanceStatisticsHost : StatisticsHostBase
{
    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, BalanceType>(nameof(BalanceType));

    public static readonly StyledProperty<BalanceDisplacementMode> BalanceDisplacementModeProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, BalanceDisplacementMode>(nameof(BalanceDisplacementMode));

    public static readonly StyledProperty<BalanceSpeedMode> BalanceSpeedModeProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, BalanceSpeedMode>(nameof(BalanceSpeedMode));

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<DampingSpeedCutoffs> PlotDampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, DampingSpeedCutoffs>(
            nameof(PlotDampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<bool> HasDynamicStatisticsProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, bool>(nameof(HasDynamicStatistics), true);

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, string?>(nameof(StaticSource));

    public static readonly StyledProperty<string?> StaticSourceNameProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, string?>(nameof(StaticSourceName));

    public static readonly StyledProperty<string?> PlotNameProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, string?>(nameof(PlotName));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, Thickness>(nameof(PlaceholderMargin));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<BalanceStatisticsHost, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

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

    public DampingSpeedCutoffs DampingSpeedCutoffs
    {
        get => GetValue(DampingSpeedCutoffsProperty);
        set => SetValue(DampingSpeedCutoffsProperty, value);
    }

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs
    {
        get => GetValue(PlotDampingSpeedCutoffsProperty);
        set => SetValue(PlotDampingSpeedCutoffsProperty, value);
    }

    public bool HasDynamicStatistics
    {
        get => GetValue(HasDynamicStatisticsProperty);
        set => SetValue(HasDynamicStatisticsProperty, value);
    }

    public string? StaticSource
    {
        get => GetValue(StaticSourceProperty);
        set => SetValue(StaticSourceProperty, value);
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

    public double PlotHeight
    {
        get => GetValue(PlotHeightProperty);
        set => SetValue(PlotHeightProperty, value);
    }

    public Thickness PlaceholderMargin
    {
        get => GetValue(PlaceholderMarginProperty);
        set => SetValue(PlaceholderMarginProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public BalanceStatisticsHost()
    {
        InitializeComponent();
    }
}
