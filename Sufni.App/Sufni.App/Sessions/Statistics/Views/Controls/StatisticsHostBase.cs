using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Statistics.Views.Controls;

public class StatisticsHostBase : UserControl
{
    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<StatisticsHostBase, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<StatisticsHostBase, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<StatisticsHostBase, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<StatisticsHostBase, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<StatisticsHostBase, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<StatisticsHostBase, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<ICommand?> SelectTelemetryRangeSelectionCommandProperty =
        AvaloniaProperty.Register<StatisticsHostBase, ICommand?>(nameof(SelectTelemetryRangeSelectionCommand));

    public static readonly StyledProperty<TelemetryRangeSelection?> SelectedFrontRangeSelectionProperty =
        AvaloniaProperty.Register<StatisticsHostBase, TelemetryRangeSelection?>(nameof(SelectedFrontRangeSelection));

    public static readonly StyledProperty<TelemetryRangeSelection?> SelectedRearRangeSelectionProperty =
        AvaloniaProperty.Register<StatisticsHostBase, TelemetryRangeSelection?>(nameof(SelectedRearRangeSelection));

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<StatisticsHostBase, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public static readonly StyledProperty<bool> HasDynamicStatisticsProperty =
        AvaloniaProperty.Register<StatisticsHostBase, bool>(nameof(HasDynamicStatistics), true);

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<StatisticsHostBase, string?>(nameof(StaticSource));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<StatisticsHostBase, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<StatisticsHostBase, Thickness>(nameof(PlaceholderMargin));

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<StatisticsHostBase, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<DampingSpeedCutoffs> PlotDampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<StatisticsHostBase, DampingSpeedCutoffs>(
            nameof(PlotDampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<StatisticsHostBase, object?>(nameof(HeaderContent));

    public static readonly DirectProperty<StatisticsHostBase, TelemetryRangeSelection?> SelectedRangeSelectionProperty =
        AvaloniaProperty.RegisterDirect<StatisticsHostBase, TelemetryRangeSelection?>(
            nameof(SelectedRangeSelection),
            host => host.SelectedRangeSelection);

    private TelemetryRangeSelection? selectedRangeSelection;
    private SuspensionType selectedRangeSelectionSuspensionType;

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
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

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public SurfacePresentationState PresentationState
    {
        get => GetValue(PresentationStateProperty);
        set => SetValue(PresentationStateProperty, value);
    }

    public TelemetryTimeRange? AnalysisRange
    {
        get => GetValue(AnalysisRangeProperty);
        set => SetValue(AnalysisRangeProperty, value);
    }

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public double MinCardHeight
    {
        get => GetValue(MinCardHeightProperty);
        set => SetValue(MinCardHeightProperty, value);
    }

    public ICommand? SelectTelemetryRangeSelectionCommand
    {
        get => GetValue(SelectTelemetryRangeSelectionCommandProperty);
        set => SetValue(SelectTelemetryRangeSelectionCommandProperty, value);
    }

    public TelemetryRangeSelection? SelectedFrontRangeSelection
    {
        get => GetValue(SelectedFrontRangeSelectionProperty);
        set => SetValue(SelectedFrontRangeSelectionProperty, value);
    }

    public TelemetryRangeSelection? SelectedRearRangeSelection
    {
        get => GetValue(SelectedRearRangeSelectionProperty);
        set => SetValue(SelectedRearRangeSelectionProperty, value);
    }

    public TelemetryRangeSelection? SelectedRangeSelection
    {
        get => selectedRangeSelection;
        private set => SetAndRaise(SelectedRangeSelectionProperty, ref selectedRangeSelection, value);
    }

    protected StatisticsHostBase()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property == SuspensionTypeProperty)
            {
                SetSelectedRangeSelectionSuspensionType(SuspensionType);
                return;
            }

            if (e.Property.Name is nameof(SelectedFrontRangeSelection) or nameof(SelectedRearRangeSelection))
            {
                UpdateSelectedRangeSelection();
            }
        };
        SetSelectedRangeSelectionSuspensionType(SuspensionType);
    }

    protected void SetSelectedRangeSelectionSuspensionType(SuspensionType suspensionType)
    {
        if (selectedRangeSelectionSuspensionType == suspensionType)
        {
            return;
        }

        selectedRangeSelectionSuspensionType = suspensionType;
        UpdateSelectedRangeSelection();
    }

    private void UpdateSelectedRangeSelection()
    {
        SelectedRangeSelection = selectedRangeSelectionSuspensionType == SuspensionType.Front
            ? SelectedFrontRangeSelection
            : SelectedRearRangeSelection;
    }
}
