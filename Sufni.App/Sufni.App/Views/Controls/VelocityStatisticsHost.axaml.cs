using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Views.Controls;

public partial class VelocityStatisticsHost : StatisticsHostBase
{
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, VelocityAverageMode>(nameof(VelocityAverageMode));

    public static readonly StyledProperty<ISessionStatisticsWorkspace?> StatisticsWorkspaceProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, ISessionStatisticsWorkspace?>(
            nameof(StatisticsWorkspace));

    public static readonly StyledProperty<bool> ShowTravelLegendProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, bool>(nameof(ShowTravelLegend));

    public static readonly StyledProperty<double?> HscPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(HscPercentage));

    public static readonly StyledProperty<double?> HsrPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(HsrPercentage));

    public static readonly StyledProperty<double?> LscPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(LscPercentage));

    public static readonly StyledProperty<double?> LsrPercentageProperty =
        AvaloniaProperty.Register<VelocityStatisticsHost, double?>(nameof(LsrPercentage));

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

    public VelocityAverageMode VelocityAverageMode
    {
        get => GetValue(VelocityAverageModeProperty);
        set => SetValue(VelocityAverageModeProperty, value);
    }

    public ISessionStatisticsWorkspace? StatisticsWorkspace
    {
        get => GetValue(StatisticsWorkspaceProperty);
        set => SetValue(StatisticsWorkspaceProperty, value);
    }

    public bool ShowTravelLegend
    {
        get => GetValue(ShowTravelLegendProperty);
        set => SetValue(ShowTravelLegendProperty, value);
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
                RefreshMetricAnnotations();
            }

            if (e.Property == ExtensionSlotsProperty)
            {
                SubscribeToSlots(ExtensionSlots);
                RefreshMetricAnnotations();
            }
        };
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
