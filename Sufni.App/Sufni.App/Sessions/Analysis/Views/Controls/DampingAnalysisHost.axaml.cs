using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Analysis.Views.Controls;

public partial class DampingAnalysisHost : AnalysisHostBase
{
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, VelocityAverageMode>(nameof(VelocityAverageMode));

    public static readonly StyledProperty<ISessionAnalysisWorkspace?> AnalysisWorkspaceProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, ISessionAnalysisWorkspace?>(
            nameof(AnalysisWorkspace));

    public static readonly StyledProperty<bool> ShowTravelLegendProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, bool>(nameof(ShowTravelLegend));

    public static readonly StyledProperty<double?> HscPercentageProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, double?>(nameof(HscPercentage));

    public static readonly StyledProperty<double?> HsrPercentageProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, double?>(nameof(HsrPercentage));

    public static readonly StyledProperty<double?> LscPercentageProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, double?>(nameof(LscPercentage));

    public static readonly StyledProperty<double?> LsrPercentageProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, double?>(nameof(LsrPercentage));

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionAnalysisMetricContribution>> HsrMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<RecordedSessionAnalysisMetricContribution>>(
            nameof(HsrMetricAnnotations),
            Array.Empty<RecordedSessionAnalysisMetricContribution>());

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionAnalysisMetricContribution>> LsrMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<RecordedSessionAnalysisMetricContribution>>(
            nameof(LsrMetricAnnotations),
            Array.Empty<RecordedSessionAnalysisMetricContribution>());

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionAnalysisMetricContribution>> LscMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<RecordedSessionAnalysisMetricContribution>>(
            nameof(LscMetricAnnotations),
            Array.Empty<RecordedSessionAnalysisMetricContribution>());

    public static readonly StyledProperty<IReadOnlyList<RecordedSessionAnalysisMetricContribution>> HscMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<RecordedSessionAnalysisMetricContribution>>(
            nameof(HscMetricAnnotations),
            Array.Empty<RecordedSessionAnalysisMetricContribution>());

    public VelocityAverageMode VelocityAverageMode
    {
        get => GetValue(VelocityAverageModeProperty);
        set => SetValue(VelocityAverageModeProperty, value);
    }

    public ISessionAnalysisWorkspace? AnalysisWorkspace
    {
        get => GetValue(AnalysisWorkspaceProperty);
        set => SetValue(AnalysisWorkspaceProperty, value);
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

    public IReadOnlyList<RecordedSessionAnalysisMetricContribution> HsrMetricAnnotations
    {
        get => GetValue(HsrMetricAnnotationsProperty);
        set => SetValue(HsrMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<RecordedSessionAnalysisMetricContribution> LsrMetricAnnotations
    {
        get => GetValue(LsrMetricAnnotationsProperty);
        set => SetValue(LsrMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<RecordedSessionAnalysisMetricContribution> LscMetricAnnotations
    {
        get => GetValue(LscMetricAnnotationsProperty);
        set => SetValue(LscMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<RecordedSessionAnalysisMetricContribution> HscMetricAnnotations
    {
        get => GetValue(HscMetricAnnotationsProperty);
        set => SetValue(HscMetricAnnotationsProperty, value);
    }

    public DampingAnalysisHost()
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
            subscribedSlots.AnalysisMetrics.CollectionChanged -= OnAnalysisMetricsChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.AnalysisMetrics.CollectionChanged += OnAnalysisMetricsChanged;
        }
    }

    private void OnAnalysisMetricsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RefreshMetricAnnotations();
    }

    private void RefreshMetricAnnotations()
    {
        HsrMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionAnalysisMetricTarget.FrontHsrPercentage
            : RecordedSessionAnalysisMetricTarget.RearHsrPercentage);
        LsrMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionAnalysisMetricTarget.FrontLsrPercentage
            : RecordedSessionAnalysisMetricTarget.RearLsrPercentage);
        LscMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionAnalysisMetricTarget.FrontLscPercentage
            : RecordedSessionAnalysisMetricTarget.RearLscPercentage);
        HscMetricAnnotations = GetMetricAnnotations(SuspensionType == SuspensionType.Front
            ? RecordedSessionAnalysisMetricTarget.FrontHscPercentage
            : RecordedSessionAnalysisMetricTarget.RearHscPercentage);
    }

    private IReadOnlyList<RecordedSessionAnalysisMetricContribution> GetMetricAnnotations(
        RecordedSessionAnalysisMetricTarget metric)
    {
        return ExtensionSlots?.AnalysisMetrics
            .Where(contribution => contribution.TargetMetric == metric)
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId)
            .ThenBy(contribution => contribution.ContributionId)
            .ToArray() ?? Array.Empty<RecordedSessionAnalysisMetricContribution>();
    }
}
