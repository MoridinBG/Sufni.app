using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Segments.Models;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Analysis.Views.Controls;

public partial class DampingAnalysisHost : AnalysisHostBase
{
    private INotifyPropertyChanged? subscribedAnalysisWorkspace;
    private bool canEditDampingSpeedCutoffs;

    public static readonly StyledProperty<VelocityAverageMode> VelocityAverageModeProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, VelocityAverageMode>(nameof(VelocityAverageMode));

    public static readonly StyledProperty<ISessionAnalysisWorkspace?> AnalysisWorkspaceProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, ISessionAnalysisWorkspace?>(
            nameof(AnalysisWorkspace));

    public static readonly DirectProperty<DampingAnalysisHost, bool> CanEditDampingSpeedCutoffsProperty =
        AvaloniaProperty.RegisterDirect<DampingAnalysisHost, bool>(
            nameof(CanEditDampingSpeedCutoffs),
            host => host.CanEditDampingSpeedCutoffs);

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

    public static readonly StyledProperty<IReadOnlyList<SegmentDampingMetric>> HsrMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<SegmentDampingMetric>>(
            nameof(HsrMetricAnnotations),
            Array.Empty<SegmentDampingMetric>());

    public static readonly StyledProperty<IReadOnlyList<SegmentDampingMetric>> LsrMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<SegmentDampingMetric>>(
            nameof(LsrMetricAnnotations),
            Array.Empty<SegmentDampingMetric>());

    public static readonly StyledProperty<IReadOnlyList<SegmentDampingMetric>> LscMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<SegmentDampingMetric>>(
            nameof(LscMetricAnnotations),
            Array.Empty<SegmentDampingMetric>());

    public static readonly StyledProperty<IReadOnlyList<SegmentDampingMetric>> HscMetricAnnotationsProperty =
        AvaloniaProperty.Register<DampingAnalysisHost, IReadOnlyList<SegmentDampingMetric>>(
            nameof(HscMetricAnnotations),
            Array.Empty<SegmentDampingMetric>());

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

    public bool CanEditDampingSpeedCutoffs
    {
        get => canEditDampingSpeedCutoffs;
        private set => SetAndRaise(CanEditDampingSpeedCutoffsProperty, ref canEditDampingSpeedCutoffs, value);
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

    public IReadOnlyList<SegmentDampingMetric> HsrMetricAnnotations
    {
        get => GetValue(HsrMetricAnnotationsProperty);
        set => SetValue(HsrMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<SegmentDampingMetric> LsrMetricAnnotations
    {
        get => GetValue(LsrMetricAnnotationsProperty);
        set => SetValue(LsrMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<SegmentDampingMetric> LscMetricAnnotations
    {
        get => GetValue(LscMetricAnnotationsProperty);
        set => SetValue(LscMetricAnnotationsProperty, value);
    }

    public IReadOnlyList<SegmentDampingMetric> HscMetricAnnotations
    {
        get => GetValue(HscMetricAnnotationsProperty);
        set => SetValue(HscMetricAnnotationsProperty, value);
    }

    public DampingAnalysisHost()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == SuspensionTypeProperty || e.Property == AnalysisComparisonProperty)
            {
                RefreshMetricAnnotations();
            }

            if (e.Property == AnalysisWorkspaceProperty)
            {
                SubscribeToAnalysisWorkspace(AnalysisWorkspace);
                RefreshCanEditDampingSpeedCutoffs();
            }
        };
        RefreshMetricAnnotations();
        RefreshCanEditDampingSpeedCutoffs();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToAnalysisWorkspace(AnalysisWorkspace);
        RefreshCanEditDampingSpeedCutoffs();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToAnalysisWorkspace(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToAnalysisWorkspace(ISessionAnalysisWorkspace? value)
    {
        if (ReferenceEquals(subscribedAnalysisWorkspace, value))
        {
            return;
        }

        if (subscribedAnalysisWorkspace is not null)
        {
            subscribedAnalysisWorkspace.PropertyChanged -= OnAnalysisWorkspacePropertyChanged;
        }

        subscribedAnalysisWorkspace = value as INotifyPropertyChanged;
        if (subscribedAnalysisWorkspace is not null)
        {
            subscribedAnalysisWorkspace.PropertyChanged += OnAnalysisWorkspacePropertyChanged;
        }
    }

    private void OnAnalysisWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionAnalysisWorkspace.CanEditDampingSpeedCutoffs))
        {
            RefreshCanEditDampingSpeedCutoffs();
        }
    }

    private void RefreshCanEditDampingSpeedCutoffs()
    {
        CanEditDampingSpeedCutoffs = AnalysisWorkspace?.CanEditDampingSpeedCutoffs == true;
    }

    private void RefreshMetricAnnotations()
    {
        HsrMetricAnnotations = GetMetricAnnotations(DampingBand.Hsr);
        LsrMetricAnnotations = GetMetricAnnotations(DampingBand.Lsr);
        LscMetricAnnotations = GetMetricAnnotations(DampingBand.Lsc);
        HscMetricAnnotations = GetMetricAnnotations(DampingBand.Hsc);
    }

    private SegmentDampingMetric[] GetMetricAnnotations(DampingBand band) =>
        AnalysisComparison?.Metrics
            .Where(metric => metric.Side == SuspensionType && metric.Band == band)
            .ToArray() ?? [];
}
