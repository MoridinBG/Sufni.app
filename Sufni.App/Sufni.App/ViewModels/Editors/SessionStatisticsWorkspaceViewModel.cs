using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

internal sealed class SessionStatisticsWorkspaceViewModel : ObservableObject, ISessionStatisticsWorkspace
{
    private readonly RecordedSessionContext context;
    private readonly ISessionOperationGateway gateway;

    public SessionStatisticsWorkspaceViewModel(
        RecordedSessionContext context,
        ISessionOperationGateway gateway,
        IRelayCommand<TelemetryRangeSelection?> selectTelemetryRangeSelectionCommand)
    {
        this.context = context;
        this.gateway = gateway;
        SelectTelemetryRangeSelectionCommand = selectTelemetryRangeSelectionCommand;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public TelemetryData? TelemetryData => context.TelemetryData;

    public TelemetryTimeRange? AnalysisRange => context.AnalysisRange;

    public TravelHistogramMode SelectedTravelHistogramMode
    {
        get => context.SelectedTravelHistogramMode;
        set => context.SelectedTravelHistogramMode = value;
    }

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => context.SelectedBalanceDisplacementMode;
        set => context.SelectedBalanceDisplacementMode = value;
    }

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => context.SelectedBalanceSpeedMode;
        set => context.SelectedBalanceSpeedMode = value;
    }

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => context.SelectedVelocityAverageMode;
        set => context.SelectedVelocityAverageMode = value;
    }

    public SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile
    {
        get => context.SelectedSessionAnalysisTargetProfile;
        set => context.SelectedSessionAnalysisTargetProfile = value;
    }

    public RecordedSessionExtensionSlots ExtensionSlots => context.ExtensionSlots;

    public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } =
        SessionAnalysisPresentation.TravelHistogramModeOptions;

    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        SessionAnalysisPresentation.BalanceDisplacementModeOptions;

    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
        SessionAnalysisPresentation.BalanceSpeedModeOptions;

    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        SessionAnalysisPresentation.VelocityAverageModeOptions;

    public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
        SessionAnalysisPresentation.SessionAnalysisTargetProfileOptions;

    public string SessionAnalysisRangeText => AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";

    public string SessionAnalysisModesText => SessionAnalysisPresentation.DescribeModes(
        SelectedTravelHistogramMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);

    public SurfacePresentationState FrontStatisticsState => context.FrontStatisticsState;

    public SurfacePresentationState RearStatisticsState => context.RearStatisticsState;

    public SurfacePresentationState CompressionBalanceState => context.CompressionBalanceState;

    public SurfacePresentationState ReboundBalanceState => context.ReboundBalanceState;

    public SurfacePresentationState FrontForkVibrationState => context.FrontForkVibrationState;

    public SurfacePresentationState FrontFrameVibrationState => context.FrontFrameVibrationState;

    public SurfacePresentationState RearForkVibrationState => context.RearForkVibrationState;

    public SurfacePresentationState RearFrameVibrationState => context.RearFrameVibrationState;

    public SessionDamperPercentages DamperPercentages => context.DamperPercentages;

    public DampingSpeedCutoffs DampingSpeedCutoffs => context.DampingSpeedCutoffs;

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs => context.PlotDampingSpeedCutoffs;

    public bool CanEditDampingSpeedCutoffs => context.CanEditDampingSpeedCutoffs;

    public SessionAnalysisResult SessionAnalysis => context.SessionAnalysis;

    public IRelayCommand<TelemetryRangeSelection?> SelectTelemetryRangeSelectionCommand { get; }

    public TelemetryRangeSelection? SelectedFrontRangeSelection => context.SelectedFrontRangeSelection;

    public TelemetryRangeSelection? SelectedRearRangeSelection => context.SelectedRearRangeSelection;

    public void PreviewDampingSpeedCutoff(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        gateway.PreviewDampingSpeedCutoff(side, circuit, cutoffMmPerSecond);
    }

    public void CancelDampingSpeedCutoffPreview()
    {
        gateway.CancelDampingSpeedCutoffPreview();
    }

    public Task CommitDampingSpeedCutoffAsync(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        return gateway.CommitDampingSpeedCutoffAsync(side, circuit, cutoffMmPerSecond);
    }

    internal static readonly HashSet<string> ForwardedProperties =
    [
        nameof(TelemetryData),
        nameof(AnalysisRange),
        nameof(SelectedTravelHistogramMode),
        nameof(SelectedBalanceDisplacementMode),
        nameof(SelectedBalanceSpeedMode),
        nameof(SelectedVelocityAverageMode),
        nameof(SelectedSessionAnalysisTargetProfile),
        nameof(ExtensionSlots),
        nameof(FrontStatisticsState),
        nameof(RearStatisticsState),
        nameof(CompressionBalanceState),
        nameof(ReboundBalanceState),
        nameof(FrontForkVibrationState),
        nameof(FrontFrameVibrationState),
        nameof(RearForkVibrationState),
        nameof(RearFrameVibrationState),
        nameof(DamperPercentages),
        nameof(DampingSpeedCutoffs),
        nameof(PlotDampingSpeedCutoffs),
        nameof(CanEditDampingSpeedCutoffs),
        nameof(SessionAnalysis),
        nameof(SelectedFrontRangeSelection),
        nameof(SelectedRearRangeSelection),
    ];

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not { } propertyName || !ForwardedProperties.Contains(propertyName))
        {
            return;
        }

        OnPropertyChanged(propertyName);
        if (propertyName is nameof(RecordedSessionContext.AnalysisRange))
        {
            OnPropertyChanged(nameof(SessionAnalysisRangeText));
            return;
        }

        if (propertyName is nameof(RecordedSessionContext.SelectedTravelHistogramMode)
            or nameof(RecordedSessionContext.SelectedVelocityAverageMode)
            or nameof(RecordedSessionContext.SelectedBalanceDisplacementMode)
            or nameof(RecordedSessionContext.SelectedBalanceSpeedMode))
        {
            OnPropertyChanged(nameof(SessionAnalysisModesText));
        }
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }
}
