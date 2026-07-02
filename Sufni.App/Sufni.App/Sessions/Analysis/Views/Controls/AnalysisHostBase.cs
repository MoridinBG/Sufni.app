using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Views.Controls;

public class AnalysisHostBase : UserControl
{
    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<AnalysisHostBase, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<AnalysisHostBase, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<AnalysisHostBase, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<AnalysisHostBase, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<AnalysisHostBase, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<AnalysisHostBase, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<ICommand?> SelectAnalysisRangeCommandProperty =
        AvaloniaProperty.Register<AnalysisHostBase, ICommand?>(nameof(SelectAnalysisRangeCommand));

    public static readonly StyledProperty<TelemetryRangeSelection?> ActiveFrontAnalysisSelectionProperty =
        AvaloniaProperty.Register<AnalysisHostBase, TelemetryRangeSelection?>(nameof(ActiveFrontAnalysisSelection));

    public static readonly StyledProperty<TelemetryRangeSelection?> ActiveRearAnalysisSelectionProperty =
        AvaloniaProperty.Register<AnalysisHostBase, TelemetryRangeSelection?>(nameof(ActiveRearAnalysisSelection));

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<AnalysisHostBase, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public static readonly StyledProperty<bool> HasAnalysisDataProperty =
        AvaloniaProperty.Register<AnalysisHostBase, bool>(nameof(HasAnalysisData), true);

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<AnalysisHostBase, string?>(nameof(StaticSource));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<AnalysisHostBase, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<AnalysisHostBase, Thickness>(nameof(PlaceholderMargin));

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<AnalysisHostBase, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<DampingSpeedCutoffs> PlotDampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<AnalysisHostBase, DampingSpeedCutoffs>(
            nameof(PlotDampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<AnalysisHostBase, object?>(nameof(HeaderContent));

    public static readonly DirectProperty<AnalysisHostBase, TelemetryRangeSelection?> ActiveAnalysisSelectionProperty =
        AvaloniaProperty.RegisterDirect<AnalysisHostBase, TelemetryRangeSelection?>(
            nameof(ActiveAnalysisSelection),
            host => host.ActiveAnalysisSelection);

    private TelemetryRangeSelection? activeAnalysisSelection;
    private SuspensionType activeAnalysisSelectionSuspensionType;

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public bool HasAnalysisData
    {
        get => GetValue(HasAnalysisDataProperty);
        set => SetValue(HasAnalysisDataProperty, value);
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

    public ICommand? SelectAnalysisRangeCommand
    {
        get => GetValue(SelectAnalysisRangeCommandProperty);
        set => SetValue(SelectAnalysisRangeCommandProperty, value);
    }

    public TelemetryRangeSelection? ActiveFrontAnalysisSelection
    {
        get => GetValue(ActiveFrontAnalysisSelectionProperty);
        set => SetValue(ActiveFrontAnalysisSelectionProperty, value);
    }

    public TelemetryRangeSelection? ActiveRearAnalysisSelection
    {
        get => GetValue(ActiveRearAnalysisSelectionProperty);
        set => SetValue(ActiveRearAnalysisSelectionProperty, value);
    }

    public TelemetryRangeSelection? ActiveAnalysisSelection
    {
        get => activeAnalysisSelection;
        private set => SetAndRaise(ActiveAnalysisSelectionProperty, ref activeAnalysisSelection, value);
    }

    protected AnalysisHostBase()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property == SuspensionTypeProperty)
            {
                SetActiveAnalysisSelectionSuspensionType(SuspensionType);
                return;
            }

            if (e.Property.Name is nameof(ActiveFrontAnalysisSelection) or nameof(ActiveRearAnalysisSelection))
            {
                UpdateActiveAnalysisSelection();
            }
        };
        SetActiveAnalysisSelectionSuspensionType(SuspensionType);
    }

    protected void SetActiveAnalysisSelectionSuspensionType(SuspensionType suspensionType)
    {
        if (activeAnalysisSelectionSuspensionType == suspensionType)
        {
            return;
        }

        activeAnalysisSelectionSuspensionType = suspensionType;
        UpdateActiveAnalysisSelection();
    }

    private void UpdateActiveAnalysisSelection()
    {
        ActiveAnalysisSelection = activeAnalysisSelectionSuspensionType == SuspensionType.Front
            ? ActiveFrontAnalysisSelection
            : ActiveRearAnalysisSelection;
    }
}
