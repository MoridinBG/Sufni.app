using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;

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
    PositionVelocity,
}

public class AnalysisPlotView : SufniTelemetryPlotView
{
    private ISessionAnalysisResultSource? subscribedAnalysisResultState;
    private IDisposable? analysisInputSubscription;
    private IDisposable? analysisResultSubscription;
    private bool hasDeferredAnalysisReload;
    private bool isStateBacked;

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

    public static readonly StyledProperty<PositionVelocitySideMode> PositionVelocitySideModeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, PositionVelocitySideMode>(
            nameof(PositionVelocitySideMode),
            PositionVelocitySideMode.Both);

    public static readonly StyledProperty<PositionVelocityDisplayMode> PositionVelocityDisplayModeProperty =
        AvaloniaProperty.Register<AnalysisPlotView, PositionVelocityDisplayMode>(
            nameof(PositionVelocityDisplayMode),
            PositionVelocityDisplayMode.Traces);

    public static readonly StyledProperty<string> AnalysisTitleProperty =
        AvaloniaProperty.Register<AnalysisPlotView, string>(nameof(AnalysisTitle), string.Empty);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<AnalysisPlotView, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<IReadOnlyList<SegmentAnalysisOverlay>?> AnalysisOverlaysProperty =
        AvaloniaProperty.Register<AnalysisPlotView, IReadOnlyList<SegmentAnalysisOverlay>?>(
            nameof(AnalysisOverlays));

    public static readonly StyledProperty<ICommand?> SelectAnalysisRangeCommandProperty =
        AvaloniaProperty.Register<AnalysisPlotView, ICommand?>(nameof(SelectAnalysisRangeCommand));

    public static readonly StyledProperty<TelemetryRangeSelection?> ActiveAnalysisSelectionProperty =
        AvaloniaProperty.Register<AnalysisPlotView, TelemetryRangeSelection?>(nameof(ActiveAnalysisSelection));

    public static readonly StyledProperty<ISessionAnalysisResultSource?> AnalysisResultStateProperty =
        AvaloniaProperty.Register<AnalysisPlotView, ISessionAnalysisResultSource?>(
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

    public PositionVelocitySideMode PositionVelocitySideMode
    {
        get => GetValue(PositionVelocitySideModeProperty);
        set => SetValue(PositionVelocitySideModeProperty, value);
    }

    public PositionVelocityDisplayMode PositionVelocityDisplayMode
    {
        get => GetValue(PositionVelocityDisplayModeProperty);
        set => SetValue(PositionVelocityDisplayModeProperty, value);
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

    /// <summary>
    /// Overlays of a compared segment match; the plot shows those of its kind and side.
    /// </summary>
    public IReadOnlyList<SegmentAnalysisOverlay>? AnalysisOverlays
    {
        get => GetValue(AnalysisOverlaysProperty);
        set => SetValue(AnalysisOverlaysProperty, value);
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

    public ISessionAnalysisResultSource? AnalysisResultState
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
                if (AnalysisResultState is not null)
                {
                    RequestAnalysisReload();
                }

                // When the state binding clears (e.g. the DataContext reverting
                // as the tab is torn down), there is nothing to reload against;
                // skip the reload so an unbind cannot drive a recompute.
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
                e.Property.Name is nameof(VelocityAverageMode) && AnalysisPlotKind == AnalysisPlotKind.VelocityDistribution ||
                e.Property.Name is nameof(PositionVelocitySideMode) or nameof(PositionVelocityDisplayMode) && AnalysisPlotKind == AnalysisPlotKind.PositionVelocity)
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
                ApplyAnalysisOverlay(refresh: true);
            }

            if (e.Property.Name is nameof(ActiveAnalysisSelection) && PlotModel is ISelectableAnalysisPlot selectablePlot)
            {
                selectablePlot.SetActiveAnalysisSelection(ActiveAnalysisSelection);
                RefreshPlot(PlotInvalidation.Overlay);
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToAnalysisResultState(AnalysisResultState);
        ApplyAnalysisOverlay(refresh: false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToAnalysisResultState(null);
        if (HasPlotModel && PlotModel is PositionVelocityPlot)
        {
            // Releases the native stroke paths while detached; the next attach reloads.
            PlotModel.Clear();
            RequestReload();
        }

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
            AnalysisPlotKind.PositionVelocity => new PositionVelocityPlot(PlotControl.Plot, CurrentTheme),
            _ => throw new ArgumentOutOfRangeException()
        };

        plotModel.ShowTitle = false;
        ApplyModeToPlotModel(plotModel);
        SetPlotModel(plotModel);
        UpdateAnalysisTitle();
        ApplyActiveAnalysisSelectionToPlotModel(plotModel);
        ApplyAnalysisOverlay(refresh: false);
        InitializeBarReadoutInteractions();
    }

    protected override void OnPlotDataLoaded()
    {
        base.OnPlotDataLoaded();
        ApplyActiveAnalysisSelectionToPlotModel(PlotModel);
        ApplyAnalysisOverlay(refresh: false);
    }

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        var key = CreateAnalysisKey();
        if (key is null || AnalysisResultState is not { } state)
        {
            // A state-backed plot publishes results asynchronously through the
            // result state. When no usable state/inputs are available — not yet
            // initialized, or torn down while the tab is closing — leave the
            // already-cleared plot and wait for the async result instead of
            // recomputing the histogram/FFT synchronously on the UI thread. Only
            // genuinely non-state-backed plots recompute directly here.
            if (isStateBacked)
            {
                return;
            }

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

        if (state is IRecordedSessionAnalysisResultState requested)
            _ = requested.RequestAsync(key);
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
        propertyName is nameof(AnalysisOverlays) or
            nameof(AnalysisPlotKind) or
            nameof(SuspensionType);

    private void SubscribeToAnalysisResultState(ISessionAnalysisResultSource? state)
    {
        // Once this plot has been given a result state it is a state-backed plot
        // for the rest of its life: results arrive asynchronously and it must not
        // fall back to synchronous recompute when the state is transiently absent.
        if (state is not null)
        {
            isStateBacked = true;
        }

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
            AnalysisPlotKind.PositionVelocity => RecordedSessionAnalysisFamily.PositionVelocity,
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
            case (PositionVelocityPlot plot, PositionVelocityAnalysisResult data):
                plot.LoadAnalysisData(data);
                break;
            default:
                break;
        }
    }

    private void ApplyAnalysisOverlay(bool refresh)
    {
        if (!HasPlotModel)
        {
            return;
        }

        SegmentAnalysisPlot? plot = AnalysisPlotKind switch
        {
            AnalysisPlotKind.TravelDistribution => SegmentAnalysisPlot.TravelDistribution,
            AnalysisPlotKind.VelocityDistribution => SegmentAnalysisPlot.VelocityDistribution,
            _ => null,
        };
        PlotModel.ApplyAnalysisOverlay(AnalysisOverlays?
            .FirstOrDefault(overlay => overlay.Plot == plot && overlay.Side == SuspensionType)?
            .Overlay);
        if (refresh)
        {
            RefreshPlot(PlotInvalidation.Overlay);
        }
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
            AnalysisPlotKind.PositionVelocity => AnalysisPlotTitles.PositionVelocity(),
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
            case PositionVelocityPlot positionVelocity:
                positionVelocity.SideMode = PositionVelocitySideMode;
                positionVelocity.DisplayMode = PositionVelocityDisplayMode;
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
            RefreshPlot(PlotInvalidation.Cursor);
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
            RefreshPlot(PlotInvalidation.Cursor);
            return;
        }

        var coordinates = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
        PlotModel.SetPointerPositionWithReadout(coordinates.X, coordinates.Y);
        RefreshPlot(PlotInvalidation.Cursor);
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
