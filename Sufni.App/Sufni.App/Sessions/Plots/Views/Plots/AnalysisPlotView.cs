using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Views.Plots;
namespace Sufni.App.Sessions.Plots.Views.Plots;

public enum AnalysisPlotKind
{
    TravelDistribution,
    TravelFrequencyDistribution,
    VelocityDistribution,
    Balance,
    StrokeLengthDistribution,
    StrokeSpeedDistribution,
    DeepTravelDistribution,
    VibrationDistribution,
}

public class AnalysisPlotView : SufniTelemetryPlotView
{
    private RecordedSessionExtensionSlots? subscribedSlots;
    private IRecordedSessionAnalysisResultState? subscribedAnalysisResultState;
    private IDisposable? analysisInputSubscription;
    private IDisposable? analysisResultSubscription;
    private bool hasDeferredAnalysisReload;

    public static readonly StyledProperty<AnalysisPlotKind> AnalysisPlotKindProperty =
        AvaloniaProperty.Register<AnalysisPlotView, AnalysisPlotKind>(nameof(AnalysisPlotKind));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, BalanceType>(nameof(BalanceType));

    public static readonly StyledProperty<ImuLocation> ImuLocationProperty =
        AvaloniaProperty.Register<AnalysisPlotView, ImuLocation>(nameof(ImuLocation));

    public static readonly StyledProperty<TravelDistributionMode> TravelDistributionModeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, TravelDistributionMode>(
            nameof(TravelDistributionMode),
            TravelDistributionMode.ActiveSuspension);

    public static readonly StyledProperty<BalanceDisplacementMode> BalanceDisplacementModeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, BalanceDisplacementMode>(
            nameof(BalanceDisplacementMode),
            BalanceDisplacementMode.Zenith);

    public static readonly StyledProperty<BalanceSpeedMode> BalanceSpeedModeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, BalanceSpeedMode>(
            nameof(BalanceSpeedMode),
            BalanceSpeedMode.Both);

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, VelocityAverageMode>(
            nameof(VelocityAverageMode),
            VelocityAverageMode.SampleAveraged);

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<AnalysisPlotView, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<string> AnalysisTitleProperty =
        AvaloniaProperty.Register<AnalysisPlotView, string>(nameof(AnalysisTitle), string.Empty);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<AnalysisPlotView, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<AnalysisPlotView, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public static readonly StyledProperty<ICommand?> SelectAnalysisRangeCommandProperty =
        AvaloniaProperty.Register<AnalysisPlotView, ICommand?>(nameof(SelectAnalysisRangeCommand));

    public static readonly StyledProperty<TelemetryRangeSelection?> ActiveAnalysisSelectionProperty =
        AvaloniaProperty.Register<AnalysisPlotView, TelemetryRangeSelection?>(nameof(ActiveAnalysisSelection));

    public static readonly StyledProperty<IRecordedSessionAnalysisResultState?> AnalysisResultStateProperty =
        AvaloniaProperty.Register<AnalysisPlotView, IRecordedSessionAnalysisResultState?>(
            nameof(AnalysisResultState));

    public static readonly StyledProperty<bool> IsAnalysisDemandActiveProperty =
        AvaloniaProperty.Register<AnalysisPlotView, bool>(nameof(IsAnalysisDemandActive), true);

    public AnalysisPlotKind AnalysisPlotKind
    {
        get => GetValue(AnalysisPlotKindProperty);
        set => SetValue(AnalysisPlotKindProperty, value);
    }

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

    public BalanceType BalanceType
    {
        get => GetValue(BalanceTypeProperty);
        set => SetValue(BalanceTypeProperty, value);
    }

    public ImuLocation ImuLocation
    {
        get => GetValue(ImuLocationProperty);
        set => SetValue(ImuLocationProperty, value);
    }

    public TravelDistributionMode TravelDistributionMode
    {
        get => GetValue(TravelDistributionModeProperty);
        set => SetValue(TravelDistributionModeProperty, value);
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

    public VelocityAverageMode VelocityAverageMode
    {
        get => GetValue(VelocityAverageModeProperty);
        set => SetValue(VelocityAverageModeProperty, value);
    }

    public DampingSpeedCutoffs DampingSpeedCutoffs
    {
        get => GetValue(DampingSpeedCutoffsProperty);
        set => SetValue(DampingSpeedCutoffsProperty, value);
    }

    public string AnalysisTitle
    {
        get => GetValue(AnalysisTitleProperty);
        set => SetValue(AnalysisTitleProperty, value);
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

    public ICommand? SelectAnalysisRangeCommand
    {
        get => GetValue(SelectAnalysisRangeCommandProperty);
        set => SetValue(SelectAnalysisRangeCommandProperty, value);
    }

    public TelemetryRangeSelection? ActiveAnalysisSelection
    {
        get => GetValue(ActiveAnalysisSelectionProperty);
        set => SetValue(ActiveAnalysisSelectionProperty, value);
    }

    public IRecordedSessionAnalysisResultState? AnalysisResultState
    {
        get => GetValue(AnalysisResultStateProperty);
        set => SetValue(AnalysisResultStateProperty, value);
    }

    public bool IsAnalysisDemandActive
    {
        get => GetValue(IsAnalysisDemandActiveProperty);
        set => SetValue(IsAnalysisDemandActiveProperty, value);
    }

    public AnalysisPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property == AnalysisResultStateProperty)
            {
                SubscribeToAnalysisResultState(AnalysisResultState);
                RequestAnalysisReload();
            }

            if (e.Property == IsAnalysisDemandActiveProperty)
            {
                ApplyDeferredAnalysisReload();
            }

            if (IsTitleProperty(e.Property.Name))
            {
                UpdateAnalysisTitle();
            }

            if (e.Property.Name is nameof(TravelDistributionMode) && AnalysisPlotKind == AnalysisPlotKind.TravelDistribution ||
                e.Property.Name is nameof(BalanceDisplacementMode) && AnalysisPlotKind == AnalysisPlotKind.Balance ||
                e.Property.Name is nameof(BalanceSpeedMode) && AnalysisPlotKind == AnalysisPlotKind.Balance ||
                e.Property.Name is nameof(DampingSpeedCutoffs) && AnalysisPlotKind is AnalysisPlotKind.Balance or AnalysisPlotKind.VelocityDistribution ||
                e.Property.Name is nameof(VelocityAverageMode) && AnalysisPlotKind == AnalysisPlotKind.VelocityDistribution)
            {
                if (!HasPlotModel)
                {
                    return;
                }

                ApplyModeToPlotModel(PlotModel);
                RequestAnalysisReload();
            }

            if (IsAnalysisOverlayProperty(e.Property.Name))
            {
                if (e.Property == ExtensionSlotsProperty)
                {
                    SubscribeToSlots(ExtensionSlots);
                }

                ApplyAnalysisOverlayDescriptor(refresh: true);
            }

            if (e.Property.Name is nameof(ActiveAnalysisSelection) && PlotModel is ISelectableAnalysisPlot selectablePlot)
            {
                selectablePlot.SetActiveAnalysisSelection(ActiveAnalysisSelection);
                RefreshPlot();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSlots(ExtensionSlots);
        SubscribeToAnalysisResultState(AnalysisResultState);
        ApplyAnalysisOverlayDescriptor(refresh: false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToSlots(null);
        SubscribeToAnalysisResultState(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void CreatePlot()
    {
        TelemetryPlot plotModel = AnalysisPlotKind switch
        {
            AnalysisPlotKind.TravelDistribution => new TravelDistributionPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            AnalysisPlotKind.TravelFrequencyDistribution => new TravelFrequencyDistributionPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            AnalysisPlotKind.VelocityDistribution => new VelocityDistributionPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            AnalysisPlotKind.Balance => new BalancePlot(PlotControl.Plot, BalanceType, CurrentTheme),
            AnalysisPlotKind.StrokeLengthDistribution => new StrokeLengthDistributionPlot(PlotControl.Plot, SuspensionType, BalanceType, CurrentTheme),
            AnalysisPlotKind.StrokeSpeedDistribution => new StrokeSpeedDistributionPlot(PlotControl.Plot, SuspensionType, BalanceType, CurrentTheme),
            AnalysisPlotKind.DeepTravelDistribution => new DeepTravelDistributionPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            AnalysisPlotKind.VibrationDistribution => new VibrationThirdsPlot(PlotControl.Plot, SuspensionType, ImuLocation, CurrentTheme),
            _ => throw new ArgumentOutOfRangeException()
        };

        plotModel.ShowTitle = false;
        ApplyModeToPlotModel(plotModel);
        SetPlotModel(plotModel);
        UpdateAnalysisTitle();
        ApplyActiveAnalysisSelectionToPlotModel(plotModel);
        ApplyAnalysisOverlayDescriptor(refresh: false);
        InitializeBarReadoutInteractions();
    }

    protected override void OnPlotDataLoaded()
    {
        base.OnPlotDataLoaded();
        ApplyActiveAnalysisSelectionToPlotModel(PlotModel);
        ApplyAnalysisOverlayDescriptor(refresh: false);
    }

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        var key = CreateAnalysisKey();
        if (key is null || AnalysisResultState is not { } state)
        {
            base.LoadPlotData(plotModel);
            return;
        }

        if (ShouldDeferAnalysisReload())
        {
            hasDeferredAnalysisReload = true;
            return;
        }

        if (state.Get(key) is { } cached)
        {
            LoadAnalysisResult(plotModel, cached);
            return;
        }

        _ = state.RequestAsync(key);
    }

    private static bool IsTitleProperty(string? propertyName) =>
        propertyName is nameof(AnalysisPlotKind) or
            nameof(SuspensionType) or
            nameof(BalanceType) or
            nameof(ImuLocation) or
            nameof(TravelDistributionMode) or
            nameof(BalanceDisplacementMode) or
            nameof(BalanceSpeedMode) or
            nameof(VelocityAverageMode);

    private static bool IsAnalysisOverlayProperty(string? propertyName) =>
        propertyName is nameof(ExtensionSlots) or
            nameof(AnalysisPlotKind) or
            nameof(SuspensionType) or
            nameof(BalanceType) or
            nameof(ImuLocation);

    private void SubscribeToAnalysisResultState(IRecordedSessionAnalysisResultState? state)
    {
        if (ReferenceEquals(subscribedAnalysisResultState, state))
        {
            return;
        }

        analysisResultSubscription?.Dispose();
        analysisResultSubscription = null;
        analysisInputSubscription?.Dispose();
        analysisInputSubscription = null;
        subscribedAnalysisResultState = state;

        if (subscribedAnalysisResultState is not null)
        {
            analysisInputSubscription = subscribedAnalysisResultState.ConnectInputs().Subscribe(_ => RequestAnalysisReload());
            analysisResultSubscription = subscribedAnalysisResultState.Connect().Subscribe(OnAnalysisResultChanged);
        }
    }

    private void OnAnalysisResultChanged(RecordedSessionAnalysisResultChanged change)
    {
        var key = CreateAnalysisKey();
        if (key is null || change.Key != key || change.Result is null)
        {
            return;
        }

        RequestAnalysisReload();
    }

    protected override void OnAnalysisRangeChanged()
    {
        if (AnalysisResultState is not null)
        {
            RequestAnalysisReload();
            return;
        }

        base.OnAnalysisRangeChanged();
    }

    private void RequestAnalysisReload()
    {
        if (ShouldDeferAnalysisReload())
        {
            hasDeferredAnalysisReload = true;
            return;
        }

        hasDeferredAnalysisReload = false;
        ReloadTelemetry();
    }

    private void ApplyDeferredAnalysisReload()
    {
        if (!IsAnalysisDemandActive || !hasDeferredAnalysisReload)
        {
            return;
        }

        RequestAnalysisReload();
    }

    private bool ShouldDeferAnalysisReload() =>
        !IsAnalysisDemandActive && AnalysisResultState is not null;

    private RecordedSessionAnalysisKey? CreateAnalysisKey()
    {
        if (AnalysisResultState?.CurrentInputs is not { } inputs)
        {
            return null;
        }

        return inputs.CreateKey(
            ResolveAnalysisFamily(),
            SuspensionType,
            BalanceType,
            ImuLocation);
    }

    private RecordedSessionAnalysisFamily ResolveAnalysisFamily()
    {
        return AnalysisPlotKind switch
        {
            AnalysisPlotKind.TravelDistribution => RecordedSessionAnalysisFamily.TravelDistribution,
            AnalysisPlotKind.TravelFrequencyDistribution => RecordedSessionAnalysisFamily.TravelFrequencyDistribution,
            AnalysisPlotKind.VelocityDistribution => RecordedSessionAnalysisFamily.VelocityDistribution,
            AnalysisPlotKind.Balance => RecordedSessionAnalysisFamily.Balance,
            AnalysisPlotKind.StrokeLengthDistribution => RecordedSessionAnalysisFamily.StrokeLengthDistribution,
            AnalysisPlotKind.StrokeSpeedDistribution => RecordedSessionAnalysisFamily.StrokeSpeedDistribution,
            AnalysisPlotKind.DeepTravelDistribution => RecordedSessionAnalysisFamily.DeepTravelDistribution,
            AnalysisPlotKind.VibrationDistribution => RecordedSessionAnalysisFamily.VibrationDistribution,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void LoadAnalysisResult(TelemetryPlot plotModel, RecordedSessionAnalysisResult result)
    {
        switch (plotModel, result)
        {
            case (TravelDistributionPlot plot, TravelDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (TravelFrequencyDistributionPlot plot, TravelFrequencyDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (VelocityDistributionPlot plot, VelocityDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (BalancePlot plot, BalanceAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (StrokeLengthDistributionPlot plot, StrokeLengthDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (StrokeSpeedDistributionPlot plot, StrokeSpeedDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (DeepTravelDistributionPlot plot, DeepTravelDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            case (VibrationThirdsPlot plot, VibrationDistributionAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            default:
                base.LoadPlotData(plotModel);
                break;
        }
    }

    private void SubscribeToSlots(RecordedSessionExtensionSlots? slots)
    {
        if (ReferenceEquals(subscribedSlots, slots))
        {
            return;
        }

        if (subscribedSlots is not null)
        {
            subscribedSlots.AnalysisOverlays.CollectionChanged -= OnAnalysisOverlaysChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.AnalysisOverlays.CollectionChanged += OnAnalysisOverlaysChanged;
        }
    }

    private void OnAnalysisOverlaysChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ApplyAnalysisOverlayDescriptor(refresh: true);
    }

    private void ApplyAnalysisOverlayDescriptor(bool refresh)
    {
        if (!HasPlotModel)
        {
            return;
        }

        PlotModel.ApplyAnalysisOverlayDescriptor(CreateAnalysisOverlayDescriptor());
        if (refresh)
        {
            RefreshPlot();
        }
    }

    private RecordedSessionAnalysisPlotOverlayDescriptor? CreateAnalysisOverlayDescriptor()
    {
        if (ExtensionSlots is null || ResolveAnalysisOverlayTarget() is not { } target)
        {
            return null;
        }

        var overlays = ExtensionSlots.AnalysisOverlays
            .Where(contribution => contribution.TargetPlot == target && contribution.Overlay is not null)
            .OrderBy(contribution => contribution.Order)
            .Select(contribution => contribution.Overlay!)
            .ToArray();

        if (overlays.Length == 0)
        {
            return null;
        }

        return new RecordedSessionAnalysisPlotOverlayDescriptor(
            overlays.SelectMany(overlay => overlay.Lines).ToArray(),
            overlays.SelectMany(overlay => overlay.Bands).ToArray(),
            overlays.SelectMany(overlay => overlay.Labels).ToArray());
    }

    private RecordedSessionAnalysisPlotTarget? ResolveAnalysisOverlayTarget()
    {
        return AnalysisPlotKind switch
        {
            AnalysisPlotKind.TravelDistribution => RecordedSessionAnalysisPlotTarget.TravelDistribution(SuspensionType),
            AnalysisPlotKind.TravelFrequencyDistribution => RecordedSessionAnalysisPlotTarget.TravelFrequencyDistribution(SuspensionType),
            AnalysisPlotKind.VelocityDistribution => RecordedSessionAnalysisPlotTarget.VelocityDistribution(SuspensionType),
            AnalysisPlotKind.Balance => RecordedSessionAnalysisPlotTarget.Balance(BalanceType),
            AnalysisPlotKind.StrokeLengthDistribution => RecordedSessionAnalysisPlotTarget.StrokeLengthDistribution(SuspensionType, BalanceType),
            AnalysisPlotKind.StrokeSpeedDistribution => RecordedSessionAnalysisPlotTarget.StrokeSpeedDistribution(SuspensionType, BalanceType),
            AnalysisPlotKind.DeepTravelDistribution => RecordedSessionAnalysisPlotTarget.DeepTravelDistribution(SuspensionType),
            AnalysisPlotKind.VibrationDistribution => RecordedSessionAnalysisPlotTarget.VibrationDistribution(SuspensionType, ImuLocation),
            _ => null,
        };
    }

    private void UpdateAnalysisTitle()
    {
        AnalysisTitle = AnalysisPlotKind switch
        {
            AnalysisPlotKind.TravelDistribution => AnalysisPlotTitles.TravelDistribution(SuspensionType, TravelDistributionMode),
            AnalysisPlotKind.TravelFrequencyDistribution => AnalysisPlotTitles.TravelFrequencyDistribution(SuspensionType),
            AnalysisPlotKind.VelocityDistribution => AnalysisPlotTitles.VelocityDistribution(SuspensionType, VelocityAverageMode),
            AnalysisPlotKind.Balance => AnalysisPlotTitles.Balance(BalanceType, BalanceDisplacementMode, BalanceSpeedMode),
            AnalysisPlotKind.StrokeLengthDistribution => AnalysisPlotTitles.StrokeLengthDistribution(SuspensionType, BalanceType),
            AnalysisPlotKind.StrokeSpeedDistribution => AnalysisPlotTitles.StrokeSpeedDistribution(SuspensionType, BalanceType),
            AnalysisPlotKind.DeepTravelDistribution => AnalysisPlotTitles.DeepTravelDistribution(SuspensionType),
            AnalysisPlotKind.VibrationDistribution => AnalysisPlotTitles.VibrationDistribution(SuspensionType, ImuLocation),
            _ => string.Empty
        };
    }

    private void ApplyModeToPlotModel(TelemetryPlot plotModel)
    {
        switch (plotModel)
        {
            case TravelDistributionPlot travelDistribution:
                travelDistribution.HistogramMode = TravelDistributionMode;
                break;
            case BalancePlot balance:
                balance.DisplacementMode = BalanceDisplacementMode;
                balance.SpeedMode = BalanceSpeedMode;
                balance.DampingSpeedCutoffs = DampingSpeedCutoffs;
                break;
            case VelocityDistributionPlot velocityDistribution:
                velocityDistribution.AverageMode = VelocityAverageMode;
                velocityDistribution.DampingSpeedCutoffs = DampingSpeedCutoffs;
                break;
        }
    }

    private void ApplyActiveAnalysisSelectionToPlotModel(TelemetryPlot plotModel)
    {
        if (plotModel is ISelectableAnalysisPlot selectablePlot)
        {
            selectablePlot.SetActiveAnalysisSelection(ActiveAnalysisSelection);
        }
    }

    private void InitializeBarReadoutInteractions()
    {
        PlotControl.PointerPressed += (_, args) =>
        {
            SetBarReadoutFromPointer(args);
            SelectRangeFromPointer(args);
        };
        PlotControl.PointerMoved += (_, args) => SetBarReadoutFromPointer(args);
        PlotControl.PointerExited += (_, _) =>
        {
            PlotModel.HideCursorReadout();
            RefreshPlot();
        };
    }

    private void SetBarReadoutFromPointer(PointerEventArgs args)
    {
        if (!HasPlotControl || !HasPlotModel)
        {
            return;
        }

        var point = args.GetPosition(PlotControl);
        if (!PlotControl.IsPointInDataArea(point))
        {
            PlotModel.HideCursorReadout();
            RefreshPlot();
            return;
        }

        var coordinates = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
        PlotModel.SetPointerPositionWithReadout(coordinates.X, coordinates.Y);
        RefreshPlot();
    }

    private void SelectRangeFromPointer(PointerEventArgs args)
    {
        if (!HasPlotControl ||
            PlotModel is not ISelectableAnalysisPlot selectablePlot ||
            SelectAnalysisRangeCommand is not { } command)
        {
            return;
        }

        var point = args.GetPosition(PlotControl);
        if (!PlotControl.IsPointInDataArea(point))
        {
            return;
        }

        var coordinates = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
        if (!selectablePlot.TryGetRangeSelection(coordinates.X, coordinates.Y, out var selection) ||
            !command.CanExecute(selection))
        {
            return;
        }

        command.Execute(selection);
    }
}
