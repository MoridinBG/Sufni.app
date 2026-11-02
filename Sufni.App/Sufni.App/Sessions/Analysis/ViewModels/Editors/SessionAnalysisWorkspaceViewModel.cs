using Sufni.App.Segments.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Common;
using Sufni.Telemetry;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Insights.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Sessions.Analysis.ViewModels.Editors;

internal sealed class SessionAnalysisWorkspaceViewModel : ObservableObject, ISessionAnalysisWorkspace, IDisposable
{
    private readonly ISessionOperationGateway gateway;
    private readonly RecordedSessionEditorActions actions;
    private readonly IDisposable stateSubscription;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();

    public SessionAnalysisWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        ISessionOperationGateway gateway,
        RecordedSessionEditorActions actions,
        IRelayCommand<TelemetryRangeSelection?> selectAnalysisRangeCommand,
        IRecordedSessionAnalysisResultState analysisResultState,
        SegmentComparisonViewModel segmentComparison)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(actions);

        this.gateway = gateway;
        this.actions = actions;
        SelectAnalysisRangeCommand = selectAnalysisRangeCommand;
        AnalysisResultState = analysisResultState;
        SegmentComparison = segmentComparison;
        PositionVelocity = new PositionVelocityPageViewModel(this);
        stateSubscription = state.Subscribe(ApplyState);
    }

    public TelemetryData? TelemetryData => currentState.TelemetryData;

    public TelemetryTimeRange? AnalysisRange => currentState.Intent.AnalysisRange;

    public TravelDistributionMode SelectedTravelDistributionMode
    {
        get => currentState.Preferences.Analysis.TravelDistributionMode;
        set => actions.SetTravelDistributionMode(value);
    }

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => currentState.Preferences.Analysis.BalanceDisplacementMode;
        set => actions.SetBalanceDisplacementMode(value);
    }

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => currentState.Preferences.Analysis.BalanceSpeedMode;
        set => actions.SetBalanceSpeedMode(value);
    }

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => currentState.Preferences.Analysis.VelocityAverageMode;
        set => actions.SetVelocityAverageMode(value);
    }

    public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile
    {
        get => currentState.Preferences.Analysis.SessionInsightsTargetProfile;
        set => actions.SetSessionInsightsTargetProfile(value);
    }

    public SegmentComparisonViewModel SegmentComparison { get; }

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

    public SurfacePresentationState FrontAnalysisState => currentState.Presentation.Analysis.FrontAnalysis;

    public SurfacePresentationState RearAnalysisState => currentState.Presentation.Analysis.RearAnalysis;

    public SurfacePresentationState CompressionBalanceState => currentState.Presentation.Analysis.CompressionBalance;

    public SurfacePresentationState ReboundBalanceState => currentState.Presentation.Analysis.ReboundBalance;

    public SurfacePresentationState FrontForkVibrationState => currentState.Presentation.Analysis.FrontForkVibration;

    public SurfacePresentationState FrontFrameVibrationState => currentState.Presentation.Analysis.FrontFrameVibration;

    public SurfacePresentationState RearForkVibrationState => currentState.Presentation.Analysis.RearForkVibration;

    public SurfacePresentationState RearFrameVibrationState => currentState.Presentation.Analysis.RearFrameVibration;

    public SessionDampingPercentages DampingPercentages => currentState.Presentation.DampingPercentages;

    public DampingSpeedCutoffs DampingSpeedCutoffs => currentState.Intent.DampingSpeedCutoffs;

    public DampingSpeedCutoffs PlotDampingSpeedCutoffs => currentState.Presentation.PlotDampingSpeedCutoffs;

    public bool CanEditDampingSpeedCutoffs => currentState.Presentation.CanEditDampingSpeedCutoffs;

    public ISessionAnalysisResultSource AnalysisResultState { get; }

    public PositionVelocityPageViewModel PositionVelocity { get; }

    public SessionInsightsResult SessionInsights => currentState.Presentation.SessionInsights;

    public void RequestSessionInsights() => gateway.RequestSessionInsights();

    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; }

    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => currentState.AnalysisSelection.ActiveFront;

    public TelemetryRangeSelection? ActiveRearAnalysisSelection => currentState.AnalysisSelection.ActiveRear;

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

    private void ApplyState(RecordedSessionEditorState state)
    {
        var previous = currentState;
        currentState = state;
        NotifyChanged(previous.TelemetryData, state.TelemetryData, nameof(TelemetryData));
        if (NotifyChanged(previous.Intent.AnalysisRange, state.Intent.AnalysisRange, nameof(AnalysisRange)))
        {
            OnPropertyChanged(nameof(SessionAnalysisRangeText));
        }

        var modesChanged =
            NotifyChanged(previous.Preferences.Analysis.TravelDistributionMode, state.Preferences.Analysis.TravelDistributionMode, nameof(SelectedTravelDistributionMode)) |
            NotifyChanged(previous.Preferences.Analysis.VelocityAverageMode, state.Preferences.Analysis.VelocityAverageMode, nameof(SelectedVelocityAverageMode)) |
            NotifyChanged(previous.Preferences.Analysis.BalanceDisplacementMode, state.Preferences.Analysis.BalanceDisplacementMode, nameof(SelectedBalanceDisplacementMode)) |
            NotifyChanged(previous.Preferences.Analysis.BalanceSpeedMode, state.Preferences.Analysis.BalanceSpeedMode, nameof(SelectedBalanceSpeedMode));
        if (modesChanged)
        {
            OnPropertyChanged(nameof(SessionAnalysisModesText));
        }

        NotifyChanged(previous.Preferences.Analysis.SessionInsightsTargetProfile, state.Preferences.Analysis.SessionInsightsTargetProfile, nameof(SelectedSessionInsightsTargetProfile));
        NotifyChanged(previous.Presentation.Analysis.FrontAnalysis, state.Presentation.Analysis.FrontAnalysis, nameof(FrontAnalysisState));
        NotifyChanged(previous.Presentation.Analysis.RearAnalysis, state.Presentation.Analysis.RearAnalysis, nameof(RearAnalysisState));
        NotifyChanged(previous.Presentation.Analysis.CompressionBalance, state.Presentation.Analysis.CompressionBalance, nameof(CompressionBalanceState));
        NotifyChanged(previous.Presentation.Analysis.ReboundBalance, state.Presentation.Analysis.ReboundBalance, nameof(ReboundBalanceState));
        NotifyChanged(previous.Presentation.Analysis.FrontForkVibration, state.Presentation.Analysis.FrontForkVibration, nameof(FrontForkVibrationState));
        NotifyChanged(previous.Presentation.Analysis.FrontFrameVibration, state.Presentation.Analysis.FrontFrameVibration, nameof(FrontFrameVibrationState));
        NotifyChanged(previous.Presentation.Analysis.RearForkVibration, state.Presentation.Analysis.RearForkVibration, nameof(RearForkVibrationState));
        NotifyChanged(previous.Presentation.Analysis.RearFrameVibration, state.Presentation.Analysis.RearFrameVibration, nameof(RearFrameVibrationState));
        NotifyChanged(previous.Presentation.DampingPercentages, state.Presentation.DampingPercentages, nameof(DampingPercentages));
        NotifyChanged(previous.Intent.DampingSpeedCutoffs, state.Intent.DampingSpeedCutoffs, nameof(DampingSpeedCutoffs));
        NotifyChanged(previous.Presentation.PlotDampingSpeedCutoffs, state.Presentation.PlotDampingSpeedCutoffs, nameof(PlotDampingSpeedCutoffs));
        NotifyChanged(previous.Presentation.CanEditDampingSpeedCutoffs, state.Presentation.CanEditDampingSpeedCutoffs, nameof(CanEditDampingSpeedCutoffs));
        NotifyChanged(previous.Presentation.SessionInsights, state.Presentation.SessionInsights, nameof(SessionInsights));
        NotifyChanged(previous.AnalysisSelection.ActiveFront, state.AnalysisSelection.ActiveFront, nameof(ActiveFrontAnalysisSelection));
        NotifyChanged(previous.AnalysisSelection.ActiveRear, state.AnalysisSelection.ActiveRear, nameof(ActiveRearAnalysisSelection));
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }
    private bool NotifyChanged<T>(T previous, T current, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(previous, current)) return false;
        OnPropertyChanged(propertyName);
        return true;
    }

}
