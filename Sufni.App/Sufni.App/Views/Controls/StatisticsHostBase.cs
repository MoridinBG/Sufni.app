using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Sufni.Telemetry;

namespace Sufni.App.Views.Controls;

public class StatisticsHostBase : UserControl
{
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
            if (e.Property.Name is nameof(SelectedFrontRangeSelection) or nameof(SelectedRearRangeSelection))
            {
                UpdateSelectedRangeSelection();
            }
        };
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
