using System;
using System.Collections.Generic;
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

internal sealed class SessionAnalysisWorkspaceViewModel : ObservableObject, ISessionAnalysisWorkspace, IDisposable
{
    private readonly ISessionOperationGateway gateway;
    private readonly RecordedSessionEditorActions actions;
    private readonly Func<RecordedSessionExtensionSlots> extensionSlots;
    private readonly IDisposable stateSubscription;
    private TelemetryData? telemetryData;
    private TelemetryTimeRange? analysisRange;
    private TravelDistributionMode selectedTravelDistributionMode = TravelDistributionMode.ActiveSuspension;
    private BalanceDisplacementMode selectedBalanceDisplacementMode = BalanceDisplacementMode.Zenith;
    private BalanceSpeedMode selectedBalanceSpeedMode = BalanceSpeedMode.Both;
    private VelocityAverageMode selectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;
    private SessionInsightsTargetProfile selectedSessionInsightsTargetProfile = SessionInsightsTargetProfile.Trail;
    private SurfacePresentationState frontAnalysisState = SurfacePresentationState.Hidden;
    private SurfacePresentationState rearAnalysisState = SurfacePresentationState.Hidden;
    private SurfacePresentationState compressionBalanceState = SurfacePresentationState.Hidden;
    private SurfacePresentationState reboundBalanceState = SurfacePresentationState.Hidden;
    private SurfacePresentationState frontForkVibrationState = SurfacePresentationState.Hidden;
    private SurfacePresentationState frontFrameVibrationState = SurfacePresentationState.Hidden;
    private SurfacePresentationState rearForkVibrationState = SurfacePresentationState.Hidden;
    private SurfacePresentationState rearFrameVibrationState = SurfacePresentationState.Hidden;
    private SessionDampingPercentages dampingPercentages = SessionDampingPercentages.Empty;
    private DampingSpeedCutoffs dampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    private DampingSpeedCutoffs plotDampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    private bool canEditDampingSpeedCutoffs;
    private SessionInsightsResult sessionInsights = SessionInsightsResult.Hidden;
    private TelemetryRangeSelection? activeFrontAnalysisSelection;
    private TelemetryRangeSelection? activeRearAnalysisSelection;

    public SessionAnalysisWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        Func<RecordedSessionExtensionSlots> extensionSlots,
        ISessionOperationGateway gateway,
        RecordedSessionEditorActions actions,
        IRelayCommand<TelemetryRangeSelection?> selectAnalysisRangeCommand,
        IRecordedSessionAnalysisResultState analysisResultState)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(extensionSlots);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(actions);

        this.extensionSlots = extensionSlots;
        this.gateway = gateway;
        this.actions = actions;
        SelectAnalysisRangeCommand = selectAnalysisRangeCommand;
        AnalysisResultState = analysisResultState;
        stateSubscription = state.Subscribe(ApplyState);
    }

    public TelemetryData? TelemetryData => telemetryData;

    public TelemetryTimeRange? AnalysisRange => analysisRange;

    public TravelDistributionMode SelectedTravelDistributionMode
    {
        get => selectedTravelDistributionMode;
        set => actions.SetTravelDistributionMode(value);
    }

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => selectedBalanceDisplacementMode;
        set => actions.SetBalanceDisplacementMode(value);
    }

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => selectedBalanceSpeedMode;
        set => actions.SetBalanceSpeedMode(value);
    }

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => selectedVelocityAverageMode;
        set => actions.SetVelocityAverageMode(value);
    }

    public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile
    {
        get => selectedSessionInsightsTargetProfile;
        set => actions.SetSessionInsightsTargetProfile(value);
    }

    public RecordedSessionExtensionSlots ExtensionSlots => extensionSlots();

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

    public SurfacePresentationState FrontAnalysisState => frontAnalysisState;

    public SurfacePresentationState RearAnalysisState => rearAnalysisState;

    public SurfacePresentationState CompressionBalanceState => compressionBalanceState;

    public SurfacePresentationState ReboundBalanceState => reboundBalanceState;

    public SurfacePresentationState FrontForkVibrationState => frontForkVibrationState;

    public SurfacePresentationState FrontFrameVibrationState => frontFrameVibrationState;

    public SurfacePresentationState RearForkVibrationState => rearForkVibrationState;

    public SurfacePresentationState RearFrameVibrationState => rearFrameVibrationState;

    public SessionDampingPercentages DampingPercentages => dampingPercentages;

    public DampingSpeedCutoffs DampingSpeedCutoffs => dampingSpeedCutoffs;

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs => plotDampingSpeedCutoffs;

    public bool CanEditDampingSpeedCutoffs => canEditDampingSpeedCutoffs;

    public IRecordedSessionAnalysisResultState AnalysisResultState { get; }

    public SessionInsightsResult SessionInsights => sessionInsights;

    public void RequestSessionInsights() => gateway.RequestSessionInsights();

    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; }

    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => activeFrontAnalysisSelection;

    public TelemetryRangeSelection? ActiveRearAnalysisSelection => activeRearAnalysisSelection;

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

    public void Dispose()
    {
        stateSubscription.Dispose();
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

    private void ApplyState(RecordedSessionEditorState state)
    {
        SetProperty(ref telemetryData, state.TelemetryData, nameof(TelemetryData));
        if (SetProperty(ref analysisRange, state.Intent.AnalysisRange, nameof(AnalysisRange)))
        {
            OnPropertyChanged(nameof(SessionAnalysisRangeText));
        }

        var modesChanged =
            SetProperty(ref selectedTravelDistributionMode, state.Intent.SelectedTravelDistributionMode, nameof(SelectedTravelDistributionMode)) |
            SetProperty(ref selectedVelocityAverageMode, state.Intent.SelectedVelocityAverageMode, nameof(SelectedVelocityAverageMode)) |
            SetProperty(ref selectedBalanceDisplacementMode, state.Intent.SelectedBalanceDisplacementMode, nameof(SelectedBalanceDisplacementMode)) |
            SetProperty(ref selectedBalanceSpeedMode, state.Intent.SelectedBalanceSpeedMode, nameof(SelectedBalanceSpeedMode));
        if (modesChanged)
        {
            OnPropertyChanged(nameof(SessionAnalysisModesText));
        }

        SetProperty(ref selectedSessionInsightsTargetProfile, state.Intent.SelectedSessionInsightsTargetProfile, nameof(SelectedSessionInsightsTargetProfile));
        SetProperty(ref frontAnalysisState, state.Presentation.Analysis.FrontAnalysis, nameof(FrontAnalysisState));
        SetProperty(ref rearAnalysisState, state.Presentation.Analysis.RearAnalysis, nameof(RearAnalysisState));
        SetProperty(ref compressionBalanceState, state.Presentation.Analysis.CompressionBalance, nameof(CompressionBalanceState));
        SetProperty(ref reboundBalanceState, state.Presentation.Analysis.ReboundBalance, nameof(ReboundBalanceState));
        SetProperty(ref frontForkVibrationState, state.Presentation.Analysis.FrontForkVibration, nameof(FrontForkVibrationState));
        SetProperty(ref frontFrameVibrationState, state.Presentation.Analysis.FrontFrameVibration, nameof(FrontFrameVibrationState));
        SetProperty(ref rearForkVibrationState, state.Presentation.Analysis.RearForkVibration, nameof(RearForkVibrationState));
        SetProperty(ref rearFrameVibrationState, state.Presentation.Analysis.RearFrameVibration, nameof(RearFrameVibrationState));
        SetProperty(ref dampingPercentages, state.Presentation.DampingPercentages, nameof(DampingPercentages));
        SetProperty(ref dampingSpeedCutoffs, state.Intent.DampingSpeedCutoffs, nameof(DampingSpeedCutoffs));
        SetProperty(ref plotDampingSpeedCutoffs, state.Presentation.PlotDampingSpeedCutoffs, nameof(PlotDampingSpeedCutoffs));
        SetProperty(ref canEditDampingSpeedCutoffs, state.Presentation.CanEditDampingSpeedCutoffs, nameof(CanEditDampingSpeedCutoffs));
        SetProperty(ref sessionInsights, state.Presentation.SessionInsights, nameof(SessionInsights));
        SetProperty(ref activeFrontAnalysisSelection, state.AnalysisSelection.ActiveFront, nameof(ActiveFrontAnalysisSelection));
        SetProperty(ref activeRearAnalysisSelection, state.AnalysisSelection.ActiveRear, nameof(ActiveRearAnalysisSelection));
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }
}
