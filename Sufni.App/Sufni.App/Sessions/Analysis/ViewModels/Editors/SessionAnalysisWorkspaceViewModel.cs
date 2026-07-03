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
using Sufni.Telemetry;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Insights.ViewModels.Editors;
namespace Sufni.App.Sessions.Analysis.ViewModels.Editors;

internal sealed class SessionAnalysisWorkspaceViewModel : ObservableObject, ISessionAnalysisWorkspace
{
    private readonly RecordedSessionContext context;
    private readonly ISessionOperationGateway gateway;

    public SessionAnalysisWorkspaceViewModel(
        RecordedSessionContext context,
        ISessionOperationGateway gateway,
        IRelayCommand<TelemetryRangeSelection?> selectAnalysisRangeCommand,
        IRecordedSessionAnalysisResultState analysisResultState)
    {
        this.context = context;
        this.gateway = gateway;
        SelectAnalysisRangeCommand = selectAnalysisRangeCommand;
        AnalysisResultState = analysisResultState;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public TelemetryData? TelemetryData => context.TelemetryData;

    public TelemetryTimeRange? AnalysisRange => context.AnalysisRange;

    public TravelDistributionMode SelectedTravelDistributionMode
    {
        get => context.SelectedTravelDistributionMode;
        set => context.SelectedTravelDistributionMode = value;
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

    public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile
    {
        get => context.SelectedSessionInsightsTargetProfile;
        set => context.SelectedSessionInsightsTargetProfile = value;
    }

    public RecordedSessionExtensionSlots ExtensionSlots => context.ExtensionSlots;

    public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } =
        SessionInsightsPresentation.TravelDistributionModeOptions;

    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        SessionInsightsPresentation.BalanceDisplacementModeOptions;

    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
        SessionInsightsPresentation.BalanceSpeedModeOptions;

    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        SessionInsightsPresentation.VelocityAverageModeOptions;

    public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } =
        SessionInsightsPresentation.SessionInsightsTargetProfileOptions;

    public string SessionAnalysisRangeText => AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";

    public string SessionAnalysisModesText => SessionInsightsPresentation.DescribeModes(
        SelectedTravelDistributionMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);

    public SurfacePresentationState FrontAnalysisState => context.FrontAnalysisState;

    public SurfacePresentationState RearAnalysisState => context.RearAnalysisState;

    public SurfacePresentationState CompressionBalanceState => context.CompressionBalanceState;

    public SurfacePresentationState ReboundBalanceState => context.ReboundBalanceState;

    public SurfacePresentationState FrontForkVibrationState => context.FrontForkVibrationState;

    public SurfacePresentationState FrontFrameVibrationState => context.FrontFrameVibrationState;

    public SurfacePresentationState RearForkVibrationState => context.RearForkVibrationState;

    public SurfacePresentationState RearFrameVibrationState => context.RearFrameVibrationState;

    public SessionDampingPercentages DampingPercentages => context.DampingPercentages;

    public DampingSpeedCutoffs DampingSpeedCutoffs => context.DampingSpeedCutoffs;

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs => context.PlotDampingSpeedCutoffs;

    public bool CanEditDampingSpeedCutoffs => context.CanEditDampingSpeedCutoffs;

    public IRecordedSessionAnalysisResultState AnalysisResultState { get; }

    public SessionInsightsResult SessionInsights => context.SessionInsights;

    public void RequestSessionInsights() => gateway.RequestSessionInsights();

    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; }

    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => context.ActiveFrontAnalysisSelection;

    public TelemetryRangeSelection? ActiveRearAnalysisSelection => context.ActiveRearAnalysisSelection;

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
        nameof(SelectedTravelDistributionMode),
        nameof(SelectedBalanceDisplacementMode),
        nameof(SelectedBalanceSpeedMode),
        nameof(SelectedVelocityAverageMode),
        nameof(SelectedSessionInsightsTargetProfile),
        nameof(ExtensionSlots),
        nameof(FrontAnalysisState),
        nameof(RearAnalysisState),
        nameof(CompressionBalanceState),
        nameof(ReboundBalanceState),
        nameof(FrontForkVibrationState),
        nameof(FrontFrameVibrationState),
        nameof(RearForkVibrationState),
        nameof(RearFrameVibrationState),
        nameof(DampingPercentages),
        nameof(DampingSpeedCutoffs),
        nameof(PlotDampingSpeedCutoffs),
        nameof(CanEditDampingSpeedCutoffs),
        nameof(SessionInsights),
        nameof(ActiveFrontAnalysisSelection),
        nameof(ActiveRearAnalysisSelection),
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

        if (propertyName is nameof(RecordedSessionContext.SelectedTravelDistributionMode)
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
