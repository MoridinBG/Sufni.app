using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.SessionDetails;

namespace Sufni.App.Views.Controls;

public partial class VelocityStatisticsHost : StatisticsHostBase
{
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<SurfacePresentationState> PresentationStateProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, SurfacePresentationState>(
            nameof(PresentationState),
            SurfacePresentationState.Hidden);

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, TelemetryTimeRange?>(nameof(AnalysisRange));

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, TelemetryData?>(nameof(Telemetry));

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, SuspensionType>(nameof(SuspensionType));

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, VelocityAverageMode>(nameof(VelocityAverageMode));

    public static readonly StyledProperty<DampingSpeedCutoffs> DampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, DampingSpeedCutoffs>(
            nameof(DampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<DampingSpeedCutoffs> PlotDampingSpeedCutoffsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, DampingSpeedCutoffs>(
            nameof(PlotDampingSpeedCutoffs),
            DampingSpeedCutoffs.Default);

    public static readonly StyledProperty<ISessionStatisticsWorkspace?> StatisticsWorkspaceProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, ISessionStatisticsWorkspace?>(
            nameof(StatisticsWorkspace));

    public static readonly StyledProperty<bool> HasDynamicStatisticsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, bool>(nameof(HasDynamicStatistics), true);

    public static readonly StyledProperty<bool> ShowTravelLegendProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, bool>(nameof(ShowTravelLegend));

    public static readonly StyledProperty<string?> StaticSourceProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, string?>(nameof(StaticSource));

    public static readonly StyledProperty<double?> HscPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(HscPercentage));

    public static readonly StyledProperty<double?> HsrPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(HsrPercentage));

    public static readonly StyledProperty<double?> LscPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(LscPercentage));

    public static readonly StyledProperty<double?> LsrPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(LsrPercentage));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinCardHeightProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double>(nameof(MinCardHeight));

    public static readonly StyledProperty<double> PlotHeightProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double>(nameof(PlotHeight), double.NaN);

    public static readonly StyledProperty<Thickness> PlaceholderMarginProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, Thickness>(nameof(PlaceholderMargin));

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionStatisticsMetricContribution>> HsrMetricAnnotationsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, IReadOnlyList<RecordedSessionStatisticsMetricContribution>>(
            nameof(HsrMetricAnnotations),
            Array.Empty<RecordedSessionStatisticsMetricContribution>());

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionStatisticsMetricContribution>> LsrMetricAnnotationsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, IReadOnlyList<RecordedSessionStatisticsMetricContribution>>(
            nameof(LsrMetricAnnotations),
            Array.Empty<RecordedSessionStatisticsMetricContribution>());

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionStatisticsMetricContribution>> LscMetricAnnotationsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, IReadOnlyList<RecordedSessionStatisticsMetricContribution>>(
            nameof(LscMetricAnnotations),
            Array.Empty<RecordedSessionStatisticsMetricContribution>());

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionStatisticsMetricContribution>> HscMetricAnnotationsProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, IReadOnlyList<RecordedSessionStatisticsMetricContribution>>(
            nameof(HscMetricAnnotations),
            Array.Empty<RecordedSessionStatisticsMetricContribution>());

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

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs
    {
        get => GetValue(PlotDampingSpeedCutoffsProperty);
        set => SetValue(PlotDampingSpeedCutoffsProperty, value);
    }

    public ISessionStatisticsWorkspace? StatisticsWorkspace
    {
        get => GetValue(StatisticsWorkspaceProperty);
        set => SetValue(StatisticsWorkspaceProperty, value);
    }

    public bool HasDynamicStatistics
    {
        get => GetValue(HasDynamicStatisticsProperty);
        set => SetValue(HasDynamicStatisticsProperty, value);
    }

    public bool ShowTravelLegend
    {
        get => GetValue(ShowTravelLegendProperty);
        set => SetValue(ShowTravelLegendProperty, value);
    }

    public string? StaticSource
    {
        get => GetValue(StaticSourceProperty);
        set => SetValue(StaticSourceProperty, value);
    }

    public double? HscPercentage
    {
        get => GetValue(HscPercentageProperty);
        set => SetValue(HscPercentageProperty, value);
    }

    public double? HsrPercentage
    {
        get => GetValue(HsrPercentageProperty);
        set => SetValue(HsrPercentageProperty, value);
    }

    public double? LscPercentage
    {
        get => GetValue(LscPercentageProperty);
        set => SetValue(LscPercentageProperty, value);
    }

    public double? LsrPercentage
    {
        get => GetValue(LsrPercentageProperty);
        set => SetValue(LsrPercentageProperty, value);
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

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public IReadOnlyList<RecordedSessionStatisticsMetricContribution> HsrMetricAnnotations
    {
        get => GetValue(HsrMetricAnnotationsProperty);
        set => SetValue(HsrMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<RecordedSessionStatisticsMetricContribution> LsrMetricAnnotations
    {
        get => GetValue(LsrMetricAnnotationsProperty);
        set => SetValue(LsrMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<RecordedSessionStatisticsMetricContribution> LscMetricAnnotations
    {
        get => GetValue(LscMetricAnnotationsProperty);
        set => SetValue(LscMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<RecordedSessionStatisticsMetricContribution> HscMetricAnnotations
    {
        get => GetValue(HscMetricAnnotationsProperty);
        set => SetValue(HscMetricAnnotationsProperty, value);
    }

    public VelocityStatisticsHost()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name is nameof(SuspensionType))
            {
                SetSelectedRangeSelectionSuspensionType(SuspensionType);
                RefreshMetricAnnotations();
            }

            if (e.Property == ExtensionSlotsProperty)
            {
                SubscribeToSlots(ExtensionSlots);
                RefreshMetricAnnotations();
            }
        };
        SetSelectedRangeSelectionSuspensionType(SuspensionType);
        RefreshMetricAnnotations();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSlots(ExtensionSlots);
        RefreshMetricAnnotations();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToSlots(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToSlots(RecordedSessionExtensionSlots? slots)
    {
        if (ReferenceEquals(subscribedSlots, slots))
        {
            return;
        }

        if (subscribedSlots is not null)
        {
            subscribedSlots.StatisticsMetrics.CollectionChanged -= OnStatisticsMetricsChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.StatisticsMetrics.CollectionChanged += OnStatisticsMetricsChanged;
        }
    }

    private void OnStatisticsMetricsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RefreshMetricAnnotations();
    }

    private void RefreshMetricAnnotations()
    {
        HsrMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionStatisticsMetricTarget.FrontHsrPercentage
            : RecordedSessionStatisticsMetricTarget.RearHsrPercentage);
        LsrMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionStatisticsMetricTarget.FrontLsrPercentage
            : RecordedSessionStatisticsMetricTarget.RearLsrPercentage);
        LscMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionStatisticsMetricTarget.FrontLscPercentage
            : RecordedSessionStatisticsMetricTarget.RearLscPercentage);
        HscMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionStatisticsMetricTarget.FrontHscPercentage
            : RecordedSessionStatisticsMetricTarget.RearHscPercentage);
    }

    private IReadOnlyList<RecordedSessionStatisticsMetricContribution> GetMetricAnnotations(
        RecordedSessionStatisticsMetricTarget metric)
    {
        return ExtensionSlots?.StatisticsMetrics
            .Where(contribution => contribution.TargetMetric == metric)
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId)
            .ThenBy(contribution => contribution.ContributionId)
            .ToArray() ?? Array.Empty<RecordedSessionStatisticsMetricContribution>();
    }
}
