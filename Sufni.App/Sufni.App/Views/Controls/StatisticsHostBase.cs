using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

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

    public static readonly DirectProperty<StatisticsHostBase, TelemetryRangeSelection?> SelectedRangeSelectionProperty =
        AvaloniaProperty.RegisterDirect<StatisticsHostBase, TelemetryRangeSelection?>(
            nameof(SelectedRangeSelection),
            host => host.SelectedRangeSelection);

    private TelemetryRangeSelection? selectedRangeSelection;
    private SuspensionType selectedRangeSelectionSuspensionType;

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
