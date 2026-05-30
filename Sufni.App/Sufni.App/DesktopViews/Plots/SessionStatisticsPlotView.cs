using System;
using Avalonia;
using Avalonia.Input;
using Sufni.App.Plots;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public enum PlotKind
{
    TravelHistogram,
    TravelFrequencyHistogram,
    VelocityHistogram,
    Balance,
    StrokeLengthHistogram,
    StrokeSpeedHistogram,
    DeepTravelHistogram,
    VibrationThirds,
}

public class SessionStatisticsPlotView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<PlotKind> PlotKindProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, PlotKind>(nameof(PlotKind));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<BalanceType> BalanceTypeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, BalanceType>(nameof(BalanceType));

    public static readonly StyledProperty<ImuLocation> ImuLocationProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, ImuLocation>(nameof(ImuLocation));

    public static readonly StyledProperty<TravelHistogramMode> TravelHistogramModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, TravelHistogramMode>(
            nameof(TravelHistogramMode),
            TravelHistogramMode.ActiveSuspension);

    public static readonly StyledProperty<BalanceDisplacementMode> BalanceDisplacementModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, BalanceDisplacementMode>(
            nameof(BalanceDisplacementMode),
            BalanceDisplacementMode.Zenith);

    public static readonly StyledProperty<BalanceSpeedMode> BalanceSpeedModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, BalanceSpeedMode>(
            nameof(BalanceSpeedMode),
            BalanceSpeedMode.Both);

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, VelocityAverageMode>(
            nameof(VelocityAverageMode),
            VelocityAverageMode.SampleAveraged);

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<string> StatisticsTitleProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, string>(nameof(StatisticsTitle), string.Empty);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<SessionStatisticsPlotView, object?>(nameof(HeaderContent));

    public PlotKind PlotKind
    {
        get => GetValue(PlotKindProperty);
        set => SetValue(PlotKindProperty, value);
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

    public TravelHistogramMode TravelHistogramMode
    {
        get => GetValue(TravelHistogramModeProperty);
        set => SetValue(TravelHistogramModeProperty, value);
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

    public string StatisticsTitle
    {
        get => GetValue(StatisticsTitleProperty);
        set => SetValue(StatisticsTitleProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public SessionStatisticsPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            if (IsTitleProperty(e.Property.Name))
            {
                UpdateStatisticsTitle();
            }

            if (e.Property.Name is nameof(TravelHistogramMode) && PlotKind == PlotKind.TravelHistogram ||
                e.Property.Name is nameof(BalanceDisplacementMode) && PlotKind == PlotKind.Balance ||
                e.Property.Name is nameof(BalanceSpeedMode) && PlotKind == PlotKind.Balance ||
                e.Property.Name is nameof(DampingSpeedCutoffs) && PlotKind is PlotKind.Balance or PlotKind.VelocityHistogram ||
                e.Property.Name is nameof(VelocityAverageMode) && PlotKind == PlotKind.VelocityHistogram)
            {
                if (!HasPlotModel)
                {
                    return;
                }

                ApplyModeToPlotModel(PlotModel);
                ReloadTelemetry();
            }
        };
    }

    protected override void CreatePlot()
    {
        TelemetryPlot plotModel = PlotKind switch
        {
            PlotKind.TravelHistogram => new TravelHistogramPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            PlotKind.TravelFrequencyHistogram => new TravelFrequencyHistogramPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            PlotKind.VelocityHistogram => new VelocityHistogramPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            PlotKind.Balance => new BalancePlot(PlotControl.Plot, BalanceType, CurrentTheme),
            PlotKind.StrokeLengthHistogram => new StrokeLengthHistogramPlot(PlotControl.Plot, SuspensionType, BalanceType, CurrentTheme),
            PlotKind.StrokeSpeedHistogram => new StrokeSpeedHistogramPlot(PlotControl.Plot, SuspensionType, BalanceType, CurrentTheme),
            PlotKind.DeepTravelHistogram => new DeepTravelHistogramPlot(PlotControl.Plot, SuspensionType, CurrentTheme),
            PlotKind.VibrationThirds => new VibrationThirdsPlot(PlotControl.Plot, SuspensionType, ImuLocation, CurrentTheme),
            _ => throw new ArgumentOutOfRangeException()
        };

        plotModel.ShowTitle = false;
        ApplyModeToPlotModel(plotModel);
        SetPlotModel(plotModel);
        UpdateStatisticsTitle();
        InitializeBarReadoutInteractions();
    }

    private static bool IsTitleProperty(string? propertyName) =>
        propertyName is nameof(PlotKind) or
            nameof(SuspensionType) or
            nameof(BalanceType) or
            nameof(ImuLocation) or
            nameof(TravelHistogramMode) or
            nameof(BalanceDisplacementMode) or
            nameof(BalanceSpeedMode) or
            nameof(VelocityAverageMode);

    private void UpdateStatisticsTitle()
    {
        StatisticsTitle = PlotKind switch
        {
            PlotKind.TravelHistogram => StatisticsPlotTitles.TravelHistogram(SuspensionType, TravelHistogramMode),
            PlotKind.TravelFrequencyHistogram => StatisticsPlotTitles.TravelFrequencyHistogram(SuspensionType),
            PlotKind.VelocityHistogram => StatisticsPlotTitles.VelocityHistogram(SuspensionType, VelocityAverageMode),
            PlotKind.Balance => StatisticsPlotTitles.Balance(BalanceType, BalanceDisplacementMode, BalanceSpeedMode),
            PlotKind.StrokeLengthHistogram => StatisticsPlotTitles.StrokeLengthHistogram(SuspensionType, BalanceType),
            PlotKind.StrokeSpeedHistogram => StatisticsPlotTitles.StrokeSpeedHistogram(SuspensionType, BalanceType),
            PlotKind.DeepTravelHistogram => StatisticsPlotTitles.DeepTravelHistogram(SuspensionType),
            PlotKind.VibrationThirds => StatisticsPlotTitles.VibrationThirds(SuspensionType, ImuLocation),
            _ => string.Empty
        };
    }

    private void ApplyModeToPlotModel(TelemetryPlot plotModel)
    {
        switch (plotModel)
        {
            case TravelHistogramPlot travelHistogram:
                travelHistogram.HistogramMode = TravelHistogramMode;
                break;
            case BalancePlot balance:
                balance.DisplacementMode = BalanceDisplacementMode;
                balance.SpeedMode = BalanceSpeedMode;
                balance.DampingSpeedCutoffs = DampingSpeedCutoffs;
                break;
            case VelocityHistogramPlot velocityHistogram:
                velocityHistogram.AverageMode = VelocityAverageMode;
                velocityHistogram.DampingSpeedCutoffs = DampingSpeedCutoffs;
                break;
        }
    }

    private void InitializeBarReadoutInteractions()
    {
        PlotControl.PointerPressed += (_, args) => SetBarReadoutFromPointer(args);
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
}
