using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed class LiveSessionAnalysisWorkspaceViewModel : ObservableObject, ISessionAnalysisWorkspace, IDisposable
{
    private readonly ILiveCaptureCoordinator coordinator;
    private readonly IBikeStore bikes;
    private readonly Guid bikeId;
    private readonly bool frontConfigured;
    private readonly bool rearConfigured;
    private readonly LiveSessionAnalysisResultSource results = new();
    private readonly DampingCutoffWorkflow cutoffs;
    private readonly CancellationTokenSource lifetime = new();
    private readonly IDisposable bikeSubscription;
    private LiveSessionAnalysis? receivedAnalysis;
    private LiveSessionHeader? header;
    private DampingSpeedCutoffs dampingCutoffs;
    private DampingSpeedCutoffs plotCutoffs;

    public LiveSessionAnalysisWorkspaceViewModel(LiveDaqSessionContext context,
        ILiveCaptureCoordinator coordinator, IBikeStore bikes, IBikeCoordinator bikeCoordinator,
        Action<FailurePresentation> reportError)
    {
        this.coordinator = coordinator;
        this.bikes = bikes;
        bikeId = context.BikeId;
        frontConfigured = context.TravelCalibration.Front is not null;
        rearConfigured = context.TravelCalibration.Rear is not null;
        dampingCutoffs = CurrentBike?.DampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        plotCutoffs = dampingCutoffs;
        cutoffs = new DampingCutoffWorkflow(() => CurrentBike, () => DampingSpeedCutoffs,
            SetCutoffs, bikeCoordinator, reportError, lifetime.Token);
        bikeSubscription = bikes.Connect().Where(changes => changes.Any(change => change.Key == bikeId)).Subscribe(_ =>
        {
            if (lifetime.IsCancellationRequested) return;
            OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
            SetCutoffs(CurrentBike?.DampingSpeedCutoffs ?? DampingSpeedCutoffs.Default, true);
        });
        coordinator.UpdateAnalysisOptions(CurrentOptions);
    }

    private BikeSnapshot? CurrentBike => bikes.Get(bikeId);
    public bool CanEditDampingSpeedCutoffs => CurrentBike is not null;
    public DampingSpeedCutoffs DampingSpeedCutoffs => dampingCutoffs;
    public DampingSpeedCutoffs PlotDampingSpeedCutoffs => plotCutoffs;
    public ISessionAnalysisResultSource AnalysisResultState => results;
    public TelemetryData? TelemetryData => receivedAnalysis?.Telemetry;
    public SessionDampingPercentages DampingPercentages => results.Current?.DampingPercentages ?? SessionDampingPercentages.Empty;
    public SegmentComparisonViewModel? SegmentComparison => null;
    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } = new RelayCommand<TelemetryRangeSelection?>(_ => { });
    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => null;
    public TelemetryRangeSelection? ActiveRearAnalysisSelection => null;
    public SurfacePresentationState FrontAnalysisState => AnalysisSurfaceState.ForSuspension(frontConfigured && TravelExpected, TelemetryData, SuspensionType.Front);
    public SurfacePresentationState RearAnalysisState => AnalysisSurfaceState.ForSuspension(rearConfigured && TravelExpected, TelemetryData, SuspensionType.Rear);
    public SurfacePresentationState CompressionBalanceState => AnalysisSurfaceState.ForBalance(frontConfigured && rearConfigured && TravelExpected, TelemetryData, BalanceType.Compression);
    public SurfacePresentationState ReboundBalanceState => AnalysisSurfaceState.ForBalance(frontConfigured && rearConfigured && TravelExpected, TelemetryData, BalanceType.Rebound);
    private bool TravelExpected => header is { AcceptedTravelHz: > 0 };

    public TravelDistributionMode SelectedTravelDistributionMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                UpdateAnalysisOptions();
            }
        }
    } = TravelDistributionMode.ActiveSuspension;

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                UpdateAnalysisOptions();
            }
        }
    } = BalanceDisplacementMode.Zenith;

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                UpdateAnalysisOptions();
            }
        }
    } = BalanceSpeedMode.Both;

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                UpdateAnalysisOptions();
            }
        }
    } = VelocityAverageMode.SampleAveraged;

    public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile
    {
        get => field;
        set => SetProperty(ref field, value);
    } = SessionInsightsTargetProfile.Trail;

    public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } = SessionInsightsPresentation.TravelDistributionModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionInsightsPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionInsightsPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionInsightsPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } = SessionInsightsPresentation.SessionInsightsTargetProfileOptions;

    public SurfacePresentationState FrontForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState FrontFrameVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearFrameVibrationState => SurfacePresentationState.Hidden;
    public TelemetryTimeRange? AnalysisRange => null;
    public SessionInsightsResult SessionInsights => SessionInsightsResult.Hidden;
    public string SessionAnalysisRangeText => "Live session";
    public string SessionAnalysisModesText => SessionInsightsPresentation.DescribeModes(
        SelectedTravelDistributionMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);

    public void Apply(LiveCaptureState state)
    {
        header = state.Controls.SessionHeader;
        receivedAnalysis = state.Analysis;
        Reconcile();
    }

    public void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double value) =>
        cutoffs.Preview(side, circuit, value);
    public void CancelDampingSpeedCutoffPreview() => cutoffs.CancelPreview();
    public Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double value) =>
        cutoffs.CommitAsync(side, circuit, value);

    private void SetCutoffs(DampingSpeedCutoffs value, bool updatePlots)
    {
        var changed = dampingCutoffs != value;
        var plotsChanged = updatePlots && plotCutoffs != value;
        dampingCutoffs = value;
        if (updatePlots) plotCutoffs = value;
        if (changed || plotsChanged) UpdateAnalysisOptions();
        if (changed) OnPropertyChanged(nameof(DampingSpeedCutoffs));
        if (plotsChanged) OnPropertyChanged(nameof(PlotDampingSpeedCutoffs));
    }

    private LiveAnalysisOptions CurrentOptions => new(SelectedTravelDistributionMode,
        SelectedVelocityAverageMode, SelectedBalanceDisplacementMode, SelectedBalanceSpeedMode,
        DampingSpeedCutoffs, PlotDampingSpeedCutoffs);

    private void UpdateAnalysisOptions()
    {
        if (lifetime.IsCancellationRequested) return;
        Reconcile();
        coordinator.UpdateAnalysisOptions(CurrentOptions);
    }

    private void Reconcile()
    {
        results.Apply(receivedAnalysis?.Options == CurrentOptions ? receivedAnalysis : null);
        OnPropertyChanged(nameof(TelemetryData));
        OnPropertyChanged(nameof(DampingPercentages));
        OnPropertyChanged(nameof(FrontAnalysisState));
        OnPropertyChanged(nameof(RearAnalysisState));
        OnPropertyChanged(nameof(CompressionBalanceState));
        OnPropertyChanged(nameof(ReboundBalanceState));
    }

    public void Dispose()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel();
        bikeSubscription.Dispose();
        results.Dispose();
        lifetime.Dispose();
    }
}
