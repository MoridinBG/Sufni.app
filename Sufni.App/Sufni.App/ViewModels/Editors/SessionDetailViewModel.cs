using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.Services;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHost.ViewModels.Editors;
using Sufni.App.ExtensionHost.Views.Controls;
using Sufni.App.ExtensionHosting.RecordedSessions;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Editor state for a recorded session's detail tab.
/// It owns loaded telemetry presentation, plot and sidebar workspace state,
/// editable notes/settings state, and reactive stale-data prompts for the
/// opened session.
/// </summary>
public sealed partial class SessionDetailViewModel : TabPageViewModelBase
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }

    public string Description => NotesPage.Description ?? "";
    public string? DescriptionText
    {
        get => NotesPage.Description;
        set => NotesPage.Description = value;
    }
    public SuspensionSettings ForkSettings => NotesPage.ForkSettings;
    public SuspensionSettings ShockSettings => NotesPage.ShockSettings;
    public RecordedSessionContext SessionContext { get; } = new();
    public ISessionShellMobileWorkspace MobileWorkspace { get; }
    public IRecordedSessionGraphWorkspace GraphWorkspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }
    public ISessionStatisticsWorkspace StatisticsWorkspace { get; }
    public ISessionSidebarWorkspace SidebarWorkspace { get; }
    public SessionTimelineLinkViewModel Timeline => SessionContext.Timeline;

    #region Private fields

    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly ISessionStore sessionStore;
    private readonly IRecordedSessionGraph recordedSessionGraph;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly ISessionAnalysisService sessionAnalysisService;
    private readonly ISessionPreferences sessionPreferences;
    private readonly RecordedSessionExtensionSlots emptyExtensionSlots = new();
    private readonly RecordedSessionExtensionManager? recordedSessionExtensions;
    private readonly Dictionary<string, PageViewModelBase> recordedSessionExtensionPages = [];
    private Session session;
    private RecordedSessionDomainSnapshot? latestDomain;
    private RecordedGraphPageViewModel GraphPage { get; }
    private StrokesPageViewModel StrokesPage { get; }
    private SpringPageViewModel SpringPage { get; }
    private BalancePageViewModel BalancePage { get; }
    private VibrationPageViewModel VibrationPage { get; }
    private SessionAnalysisPageViewModel AnalysisPage { get; }

    private readonly CancellableOperation loadOperation = new();
    private bool lastObservedHasProcessedData;
    private SessionPresentationDimensions? lastPresentationDimensions;
    private double? pendingAnalysisRangeBoundary;
    private bool suppressDirtinessEvaluation;
    private bool suppressAnalysisRecompute;
    private bool observedInitialDomain;
    private bool recomputePromptRunning;
    private bool processingPreferenceRecomputeRunning;
    private string? promptedRecomputeSignature;
    private RecordedSessionDomainSnapshot? deferredDomainWhileInactive;
    private bool reportedNotRecomputableStale;
    private bool recordedPreferencePersistenceEnabled; // Prevent property set on creation from re-writing preferences
    private bool viewLoaded;
    private bool hasBeenActivated;
    private SessionPreferences recordedPreferences = SessionPreferences.Default;
    private SessionPlotPreferences plotPreferences = SessionPreferences.Default.Plots;
    private SessionGraphPreferences graphPreferences = SessionPreferences.Default.Graph;
    private readonly TelemetryPlotRowAction showAirtimeAction;
    private readonly TelemetryPlotRowAction showVelocityAirtimeAction;
    private readonly TelemetryPlotRowAction showImuAirtimeAction;
    private readonly TelemetryPlotRowAction showPitchRollAirtimeAction;
    private readonly TelemetryPlotRowAction showSpeedAirtimeAction;
    private readonly TelemetryPlotRowAction showElevationAirtimeAction;
    private readonly TelemetryPlotRowAction showStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showVelocityStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showImuStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showPitchRollStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showSpeedStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showElevationStatisticsSelectionAction;
    private readonly PlotAutozoomController plotAutozoomController;
    private TelemetryRangeSelection? frontTelemetryRangeSelection;
    private TelemetryRangeSelection? rearTelemetryRangeSelection;
    private SurfacePresentationState recordedTravelGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedVelocityGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedImuGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedPitchRollGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedSpeedGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedElevationGraphBaseState = SurfacePresentationState.Hidden;
    private DampingSpeedCutoffOwner? dampingSpeedCutoffOwner;
    private DampingSpeedCutoffs persistedDampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    private DampingSpeedCutoffs? dampingSpeedCutoffPreviewOrigin;

    #endregion Private fields

    #region Public fields

    public DamperPageViewModel DamperPage { get; }
    public bool HasMediaContent =>
        MapState.ReservesLayout ||
        VideoState.ReservesLayout ||
        ExtensionSlots.MediaPanes.Count > 0;
    public NotesPageViewModel NotesPage { get; } = new();
    public SessionPlotPreferences PlotPreferences
    {
        get => plotPreferences;
        private set
        {
            if (SetProperty(ref plotPreferences, value))
            {
                SessionContext.PlotPreferences = value;
            }
        }
    }

    public SessionGraphPreferences GraphPreferences
    {
        get => graphPreferences;
        set
        {
            if (!SetProperty(ref graphPreferences, value))
            {
                return;
            }

            recordedPreferences = recordedPreferences with { Graph = value };
            SessionContext.GraphPreferences = value;
            PersistRecordedPreferenceChangeIfEnabled(current => current with { Graph = value });
        }
    }
    public TelemetrySourceVisibilityStore SourceVisibility => SessionContext.SourceVisibility;
    public PreferencesPageViewModel PreferencesPage { get; } = new();
    public MapViewModel? MapViewModel => SessionContext.MapViewModel;
    public IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions { get; }
    public bool HasStatisticsSelection => StatisticsSelectionHighlightRanges.Count > 0;
    public TelemetryRangeSelection? SelectedFrontRangeSelection => frontTelemetryRangeSelection;
    public TelemetryRangeSelection? SelectedRearRangeSelection => rearTelemetryRangeSelection;
    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> PlotContextMenuActionsByRowId { get; }
    public bool CanEditDampingSpeedCutoffs => dampingSpeedCutoffOwner is not null;
    public RecordedSessionExtensionSlots ExtensionSlots => recordedSessionExtensions?.ExtensionSlots ?? emptyExtensionSlots;

    #endregion Public fields

    #region Observable properties

    [ObservableProperty] private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;
    [ObservableProperty] private SessionOperationPresentationState sessionOperationState = SessionOperationPresentationState.Hidden;
    [ObservableProperty] private TelemetryData? telemetryData;
    [ObservableProperty] private TelemetryTimeRange? analysisRange;
    [ObservableProperty] private TrackTimeRange? trackTimelineContext;
    [ObservableProperty] private List<TrackPoint>? fullTrackPoints;
    [ObservableProperty] private List<TrackPoint>? trackPoints;
    [ObservableProperty] private string? videoUrl;
    [ObservableProperty] private double? mapVideoWidth;
    [ObservableProperty] private bool isComplete;
    [ObservableProperty] private SurfacePresentationState travelGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState velocityGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState imuGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState pitchRollGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState speedGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState elevationGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontStatisticsState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearStatisticsState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState compressionBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState reboundBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontForkVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontFrameVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearForkVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearFrameVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private bool showAirtime = true;
    [ObservableProperty] private bool showVelocityAirtime;
    [ObservableProperty] private bool showImuAirtime;
    [ObservableProperty] private bool showPitchRollAirtime;
    [ObservableProperty] private bool showSpeedAirtime;
    [ObservableProperty] private bool showElevationAirtime;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatisticsSelection))]
    private IReadOnlyList<TelemetryHighlightRange> statisticsSelectionHighlightRanges = [];
    [ObservableProperty] private bool showStatisticsSelection;
    [ObservableProperty] private bool showVelocityStatisticsSelection;
    [ObservableProperty] private bool showImuStatisticsSelection;
    [ObservableProperty] private bool showPitchRollStatisticsSelection;
    [ObservableProperty] private bool showSpeedStatisticsSelection;
    [ObservableProperty] private bool showElevationStatisticsSelection;
    [ObservableProperty] private TravelHistogramMode selectedTravelHistogramMode = TravelHistogramMode.ActiveSuspension;
    [ObservableProperty] private BalanceDisplacementMode selectedBalanceDisplacementMode = BalanceDisplacementMode.Zenith;
    [ObservableProperty] private BalanceSpeedMode selectedBalanceSpeedMode = BalanceSpeedMode.Both;
    [ObservableProperty] private VelocityAverageMode selectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;
    [ObservableProperty] private SessionAnalysisTargetProfile selectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMediaContent))]
    private SurfacePresentationState mapState = SurfacePresentationState.Hidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMediaContent))]
    private SurfacePresentationState videoState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SessionDamperPercentages damperPercentages = SessionDamperPercentages.Empty;
    [ObservableProperty] private DampingSpeedCutoffs dampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    [ObservableProperty] private DampingSpeedCutoffs plotDampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    [ObservableProperty] private SessionAnalysisResult sessionAnalysis = SessionAnalysisResult.Hidden;
    public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } = SessionAnalysisPresentation.TravelHistogramModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionAnalysisPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionAnalysisPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionAnalysisPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } = SessionAnalysisPresentation.SessionAnalysisTargetProfileOptions;
    public string SessionAnalysisRangeText => AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";
    public string SessionAnalysisModesText => SessionAnalysisPresentation.DescribeModes(
        SelectedTravelHistogramMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);
    public ObservableCollection<PageViewModelBase> Pages => SessionContext.Pages;

    #endregion Observable properties

    partial void OnScreenStateChanged(SessionScreenPresentationState value)
    {
        SessionContext.ScreenState = value;
    }

    partial void OnSessionOperationStateChanged(SessionOperationPresentationState value)
    {
        SessionContext.SessionOperationState = value;
    }

    partial void OnTravelGraphStateChanged(SurfacePresentationState value)
    {
        SessionContext.TravelGraphState = value;
    }

    partial void OnVelocityGraphStateChanged(SurfacePresentationState value)
    {
        SessionContext.VelocityGraphState = value;
    }

    partial void OnImuGraphStateChanged(SurfacePresentationState value)
    {
        SessionContext.ImuGraphState = value;
    }

    partial void OnPitchRollGraphStateChanged(SurfacePresentationState value)
    {
        SessionContext.PitchRollGraphState = value;
    }

    partial void OnSpeedGraphStateChanged(SurfacePresentationState value)
    {
        SessionContext.SpeedGraphState = value;
    }

    partial void OnElevationGraphStateChanged(SurfacePresentationState value)
    {
        SessionContext.ElevationGraphState = value;
    }

    partial void OnFrontStatisticsStateChanged(SurfacePresentationState value)
    {
        SessionContext.FrontStatisticsState = value;
    }

    partial void OnRearStatisticsStateChanged(SurfacePresentationState value)
    {
        SessionContext.RearStatisticsState = value;
    }

    partial void OnCompressionBalanceStateChanged(SurfacePresentationState value)
    {
        SessionContext.CompressionBalanceState = value;
    }

    partial void OnReboundBalanceStateChanged(SurfacePresentationState value)
    {
        SessionContext.ReboundBalanceState = value;
    }

    partial void OnFrontForkVibrationStateChanged(SurfacePresentationState value)
    {
        SessionContext.FrontForkVibrationState = value;
    }

    partial void OnFrontFrameVibrationStateChanged(SurfacePresentationState value)
    {
        SessionContext.FrontFrameVibrationState = value;
    }

    partial void OnRearForkVibrationStateChanged(SurfacePresentationState value)
    {
        SessionContext.RearForkVibrationState = value;
    }

    partial void OnRearFrameVibrationStateChanged(SurfacePresentationState value)
    {
        SessionContext.RearFrameVibrationState = value;
    }

    partial void OnTelemetryDataChanged(TelemetryData? value)
    {
        SessionContext.TelemetryData = value;
        IsComplete = value != null;
        NotesPage.SetTemperatureAverages(value?.TemperatureAverages ?? []);
        pendingAnalysisRangeBoundary = null;
        ClearStatisticsSelections();
        RefreshTrackTimelineContext();
        if (value is null)
        {
            SessionAnalysis = SessionAnalysisResult.Hidden;
            UpdateRecordedSessionExtensionHostState();
            return;
        }

        if (AnalysisRange is not null)
        {
            ClearAnalysisRange();
            return;
        }

        RecomputeDamperPercentagesForAnalysisRange();
        RecomputeSessionAnalysisIfAllowed();
        UpdateRecordedSessionExtensionHostState();
    }

    partial void OnAnalysisRangeChanged(TelemetryTimeRange? value)
    {
        SessionContext.AnalysisRange = value;
        OnPropertyChanged(nameof(SessionAnalysisRangeText));
        ClearStatisticsSelections();
        RefreshAnalysisRangeStates();
        RecomputeDamperPercentagesForAnalysisRange();
        RecomputeSessionAnalysisIfAllowed();
        UpdateRecordedSessionExtensionHostState();
    }

    partial void OnSelectedTravelHistogramModeChanged(TravelHistogramMode value)
    {
        SessionContext.SelectedTravelHistogramMode = value;
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
        UpdateRecordedSessionExtensionHostState();
    }

    partial void OnSelectedBalanceDisplacementModeChanged(BalanceDisplacementMode value)
    {
        SessionContext.SelectedBalanceDisplacementMode = value;
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnSelectedBalanceSpeedModeChanged(BalanceSpeedMode value)
    {
        SessionContext.SelectedBalanceSpeedMode = value;
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnSelectedVelocityAverageModeChanged(VelocityAverageMode value)
    {
        SessionContext.SelectedVelocityAverageMode = value;
        ClearDampingRangeSelections();
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeDamperPercentagesForAnalysisRange();
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
        UpdateRecordedSessionExtensionHostState();
    }

    partial void OnSelectedSessionAnalysisTargetProfileChanged(SessionAnalysisTargetProfile value)
    {
        SessionContext.SelectedSessionAnalysisTargetProfile = value;
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnDampingSpeedCutoffsChanged(DampingSpeedCutoffs value)
    {
        SessionContext.DampingSpeedCutoffs = value;
        RecomputeDamperPercentagesForAnalysisRange();
        RecomputeSessionAnalysisIfAllowed();
        UpdateRecordedSessionExtensionHostState();
    }

    partial void OnDamperPercentagesChanged(SessionDamperPercentages value)
    {
        SessionContext.DamperPercentages = value;
    }

    partial void OnPlotDampingSpeedCutoffsChanged(DampingSpeedCutoffs value)
    {
        SessionContext.PlotDampingSpeedCutoffs = value;
    }

    partial void OnSessionAnalysisChanged(SessionAnalysisResult value)
    {
        SessionContext.SessionAnalysis = value;
    }

    partial void OnFullTrackPointsChanged(List<TrackPoint>? value)
    {
        SessionContext.FullTrackPoints = value;
        if (MapViewModel is null)
        {
            return;
        }

        MapViewModel.FullTrackPoints = value;
    }

    partial void OnTrackPointsChanged(List<TrackPoint>? value)
    {
        SessionContext.TrackPoints = value;
        if (MapViewModel is not null)
        {
            MapViewModel.SessionTrackPoints = value;
        }

        RefreshTrackTimelineContext();
        if (TelemetryData is not null)
        {
            ApplyRecordedTrackGraphStates();
        }
    }

    partial void OnTrackTimelineContextChanged(TrackTimeRange? value)
    {
        SessionContext.TrackTimelineContext = value;
        if (MapViewModel is not null)
        {
            MapViewModel.TimelineContext = value;
        }

        UpdateRecordedSessionExtensionHostState();
    }

    partial void OnShowAirtimeChanged(bool value)
    {
        SessionContext.ShowAirtime = value;
        UpdateAirtimeAction(showAirtimeAction, value);
    }

    partial void OnShowVelocityAirtimeChanged(bool value)
    {
        SessionContext.ShowVelocityAirtime = value;
        UpdateAirtimeAction(showVelocityAirtimeAction, value);
    }

    partial void OnShowImuAirtimeChanged(bool value)
    {
        SessionContext.ShowImuAirtime = value;
        UpdateAirtimeAction(showImuAirtimeAction, value);
    }

    partial void OnShowPitchRollAirtimeChanged(bool value)
    {
        SessionContext.ShowPitchRollAirtime = value;
        UpdateAirtimeAction(showPitchRollAirtimeAction, value);
    }

    partial void OnShowSpeedAirtimeChanged(bool value)
    {
        SessionContext.ShowSpeedAirtime = value;
        UpdateAirtimeAction(showSpeedAirtimeAction, value);
    }

    partial void OnShowElevationAirtimeChanged(bool value)
    {
        SessionContext.ShowElevationAirtime = value;
        UpdateAirtimeAction(showElevationAirtimeAction, value);
    }

    partial void OnVideoUrlChanged(string? value)
    {
        SessionContext.VideoUrl = value;
        VideoState = string.IsNullOrWhiteSpace(value)
            ? SurfacePresentationState.Hidden
            : SurfacePresentationState.Ready;
    }

    partial void OnMapVideoWidthChanged(double? value)
    {
        SessionContext.MapVideoWidth = value;
    }

    partial void OnMapStateChanged(SurfacePresentationState value)
    {
        SessionContext.MapState = value;
    }

    partial void OnVideoStateChanged(SurfacePresentationState value)
    {
        SessionContext.VideoState = value;
    }

    partial void OnStatisticsSelectionHighlightRangesChanged(IReadOnlyList<TelemetryHighlightRange> value)
    {
        SessionContext.StatisticsSelectionHighlightRanges = value;
    }

    partial void OnShowStatisticsSelectionChanged(bool value)
    {
        SessionContext.ShowStatisticsSelection = value;
        UpdateStatisticsSelectionAction(showStatisticsSelectionAction, value, HasStatisticsSelection);
    }

    partial void OnShowVelocityStatisticsSelectionChanged(bool value)
    {
        SessionContext.ShowVelocityStatisticsSelection = value;
        UpdateStatisticsSelectionAction(showVelocityStatisticsSelectionAction, value, HasStatisticsSelection);
    }

    partial void OnShowImuStatisticsSelectionChanged(bool value)
    {
        SessionContext.ShowImuStatisticsSelection = value;
        UpdateStatisticsSelectionAction(showImuStatisticsSelectionAction, value, HasStatisticsSelection);
    }

    partial void OnShowPitchRollStatisticsSelectionChanged(bool value)
    {
        SessionContext.ShowPitchRollStatisticsSelection = value;
        UpdateStatisticsSelectionAction(showPitchRollStatisticsSelectionAction, value, HasStatisticsSelection);
    }

    partial void OnShowSpeedStatisticsSelectionChanged(bool value)
    {
        SessionContext.ShowSpeedStatisticsSelection = value;
        UpdateStatisticsSelectionAction(showSpeedStatisticsSelectionAction, value, HasStatisticsSelection);
    }

    partial void OnShowElevationStatisticsSelectionChanged(bool value)
    {
        SessionContext.ShowElevationStatisticsSelection = value;
        UpdateStatisticsSelectionAction(showElevationStatisticsSelectionAction, value, HasStatisticsSelection);
    }

    #region Private methods

    private static SessionPresentationDimensions? CreatePresentationDimensions(Rect? bounds)
    {
        if (bounds is not Rect rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        return new SessionPresentationDimensions((int)rect.Width, (int)(rect.Height / 2.0));
    }

    private void ApplyDamperPercentages(SessionDamperPercentages percentages)
    {
        DamperPercentages = percentages;
        DamperPage.ApplyDamperPercentages(percentages);
        UpdateRecordedSessionExtensionHostState();
    }

    private void ApplyDampingSpeedCutoffContext(
        DampingSpeedCutoffs cutoffs,
        DampingSpeedCutoffOwner? owner)
    {
        persistedDampingSpeedCutoffs = cutoffs.ClampValues();
        dampingSpeedCutoffPreviewOrigin = null;
        dampingSpeedCutoffOwner = owner;
        SessionContext.CanEditDampingSpeedCutoffs = dampingSpeedCutoffOwner is not null;
        OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
        PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        DampingSpeedCutoffs = persistedDampingSpeedCutoffs;
    }

    public void PreviewDampingSpeedCutoff(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        if (dampingSpeedCutoffOwner is null)
        {
            return;
        }

        dampingSpeedCutoffPreviewOrigin ??= DampingSpeedCutoffs;
        DampingSpeedCutoffs = DampingSpeedCutoffs.With(
            side,
            circuit,
            DampingSpeedCutoffs.RoundDragValue(cutoffMmPerSecond));
    }

    public void CancelDampingSpeedCutoffPreview()
    {
        if (dampingSpeedCutoffPreviewOrigin is not { } origin)
        {
            return;
        }

        dampingSpeedCutoffPreviewOrigin = null;
        DampingSpeedCutoffs = origin;
    }

    public async Task CommitDampingSpeedCutoffAsync(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        if (dampingSpeedCutoffOwner is not { } owner || bikeCoordinator is null)
        {
            return;
        }

        dampingSpeedCutoffPreviewOrigin = null;
        var committedCutoffs = DampingSpeedCutoffs.With(
            side,
            circuit,
            DampingSpeedCutoffs.RoundDragValue(cutoffMmPerSecond));
        DampingSpeedCutoffs = committedCutoffs;
        PlotDampingSpeedCutoffs = committedCutoffs;

        var result = await bikeCoordinator.UpdateDampingSpeedCutoffAsync(
            owner.BikeId,
            owner.BaselineUpdated,
            side,
            circuit,
            committedCutoffs.Get(side, circuit));

        switch (result)
        {
            case BikeDampingSpeedCutoffUpdateResult.Saved saved:
                ApplyDampingSpeedCutoffContext(
                    saved.Snapshot.DampingSpeedCutoffs,
                    new DampingSpeedCutoffOwner(saved.Snapshot.Id, saved.Snapshot.Updated));
                break;

            case BikeDampingSpeedCutoffUpdateResult.Conflict conflict:
                ApplyDampingSpeedCutoffContext(
                    conflict.CurrentSnapshot.DampingSpeedCutoffs,
                    new DampingSpeedCutoffOwner(conflict.CurrentSnapshot.Id, conflict.CurrentSnapshot.Updated));
                ErrorMessages.Add("Bike damping cutoff changed elsewhere. Reloaded the latest cutoff.");
                break;

            case BikeDampingSpeedCutoffUpdateResult.Failed failed:
                DampingSpeedCutoffs = persistedDampingSpeedCutoffs;
                PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
                ErrorMessages.Add($"Could not save damping cutoff: {failed.ErrorMessage}");
                break;
        }
    }

    private void ClearDamperPercentages()
    {
        ApplyDamperPercentages(SessionDamperPercentages.Empty);
    }

    private void RecomputeDamperPercentagesForAnalysisRange()
    {
        if (TelemetryData is null)
        {
            ClearDamperPercentages();
            return;
        }

        ApplyDamperPercentages(sessionPresentationService.CalculateDamperPercentages(
            TelemetryData,
            AnalysisRange,
            SelectedVelocityAverageMode,
            DampingSpeedCutoffs));
    }

    private void ApplyModeAwareDamperPercentages(SessionDamperPercentages sampleAveragedPercentages)
    {
        if (TelemetryData is null)
        {
            ClearDamperPercentages();
            return;
        }

        if (AnalysisRange is null && SelectedVelocityAverageMode == VelocityAverageMode.SampleAveraged)
        {
            ApplyDamperPercentages(sampleAveragedPercentages);
            return;
        }

        RecomputeDamperPercentagesForAnalysisRange();
    }

    private void RecomputeSessionAnalysisIfAllowed()
    {
        if (suppressAnalysisRecompute)
        {
            return;
        }

        RecomputeSessionAnalysis();
    }

    private void RecomputeSessionAnalysis()
    {
        SessionAnalysis = sessionAnalysisService.Analyze(new SessionAnalysisRequest(
            TelemetryData,
            AnalysisRange,
            SelectedTravelHistogramMode,
            SelectedVelocityAverageMode,
            SelectedBalanceDisplacementMode,
            SelectedBalanceSpeedMode,
            DamperPercentages,
            SelectedSessionAnalysisTargetProfile)
        {
            DampingSpeedCutoffs = this.DampingSpeedCutoffs,
        });
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }

    private void EnsureBalancePage(bool balanceAvailable)
    {
        var containsBalancePage = Pages.Contains(BalancePage);
        if (balanceAvailable)
        {
            if (containsBalancePage)
            {
                return;
            }

            var insertIndex = Pages.IndexOf(VibrationPage);
            if (insertIndex < 0)
            {
                insertIndex = Pages.IndexOf(AnalysisPage);
            }

            if (insertIndex < 0)
            {
                insertIndex = Pages.IndexOf(NotesPage);
            }

            if (insertIndex < 0)
            {
                Pages.Add(BalancePage);
            }
            else
            {
                Pages.Insert(insertIndex, BalancePage);
            }

            return;
        }

        if (containsBalancePage)
        {
            Pages.Remove(BalancePage);
        }
    }

    private void ApplyCachePresentation(SessionCachePresentationData data)
    {
        ApplyDampingSpeedCutoffContext(data.DampingSpeedCutoffs, data.DampingSpeedCutoffOwner);

        var hasFrontTravelHistogram = !string.IsNullOrWhiteSpace(data.FrontTravelHistogram);
        var hasRearTravelHistogram = !string.IsNullOrWhiteSpace(data.RearTravelHistogram);
        var hasFrontVelocityHistogram = !string.IsNullOrWhiteSpace(data.FrontVelocityHistogram);
        var hasRearVelocityHistogram = !string.IsNullOrWhiteSpace(data.RearVelocityHistogram);
        var hasCompressionBalance = !string.IsNullOrWhiteSpace(data.CompressionBalance);
        var hasReboundBalance = !string.IsNullOrWhiteSpace(data.ReboundBalance);

        SpringPage.FrontTravelHistogram = data.FrontTravelHistogram;
        SpringPage.RearTravelHistogram = data.RearTravelHistogram;
        SpringPage.FrontHistogramState = hasFrontTravelHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        SpringPage.RearHistogramState = hasRearTravelHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        DamperPage.FrontVelocityHistogram = data.FrontVelocityHistogram;
        DamperPage.RearVelocityHistogram = data.RearVelocityHistogram;
        DamperPage.FrontHistogramState = hasFrontVelocityHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        DamperPage.RearHistogramState = hasRearVelocityHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        FrontStatisticsState = SpringPage.FrontHistogramState.ReservesLayout || DamperPage.FrontHistogramState.ReservesLayout
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        RearStatisticsState = SpringPage.RearHistogramState.ReservesLayout || DamperPage.RearHistogramState.ReservesLayout
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        ApplyDamperPercentages(data.DamperPercentages);
        BalancePage.CompressionBalance = data.CompressionBalance;
        BalancePage.ReboundBalance = data.ReboundBalance;
        BalancePage.CompressionBalanceState = hasCompressionBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        BalancePage.ReboundBalanceState = hasReboundBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        CompressionBalanceState = BalancePage.CompressionBalanceState;
        ReboundBalanceState = BalancePage.ReboundBalanceState;
        HideVibrationStates();
        EnsureBalancePage(data.BalanceAvailable);
    }

    private static bool HasTravelTelemetry(TelemetryData? telemetry)
    {
        return telemetry is { } value && (value.Front.Present || value.Rear.Present);
    }

    private static bool HasImuTelemetry(TelemetryData? telemetry)
    {
        return telemetry?.ImuData is { } imuData &&
               imuData.Records.Count > 0 &&
               imuData.ActiveLocations.Count > 0;
    }

    private static bool HasFramePitchRollTelemetry(TelemetryData? telemetry)
    {
        if (telemetry?.ImuData is not { } imuData ||
            imuData.Records.Count == 0 ||
            !imuData.ActiveLocations.Contains((byte)ImuLocation.Frame))
        {
            return false;
        }

        var frameMeta = imuData.Meta.FirstOrDefault(meta => meta.LocationId == (byte)ImuLocation.Frame);
        return frameMeta is { AccelLsbPerG: > 0, GyroLsbPerDps: > 0 };
    }

    private void RefreshAnalysisRangeStates()
    {
        if (TelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
    }

    private void ApplyAnalysisRangeStates(TelemetryData telemetry)
    {
        FrontStatisticsState = SessionStatisticsSurfaceState.ForSuspension(telemetry, SuspensionType.Front, AnalysisRange);
        RearStatisticsState = SessionStatisticsSurfaceState.ForSuspension(telemetry, SuspensionType.Rear, AnalysisRange);
        CompressionBalanceState = SessionStatisticsSurfaceState.ForBalance(telemetry, BalanceType.Compression, AnalysisRange);
        ReboundBalanceState = SessionStatisticsSurfaceState.ForBalance(telemetry, BalanceType.Rebound, AnalysisRange);
        FrontForkVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Fork, AnalysisRange);
        FrontFrameVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Frame, AnalysisRange);
        RearForkVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Fork, AnalysisRange);
        RearFrameVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Frame, AnalysisRange);
    }

    private void HideVibrationStates()
    {
        FrontForkVibrationState = SurfacePresentationState.Hidden;
        FrontFrameVibrationState = SurfacePresentationState.Hidden;
        RearForkVibrationState = SurfacePresentationState.Hidden;
        RearFrameVibrationState = SurfacePresentationState.Hidden;
    }

    private static SurfacePresentationState CreateMapState(IReadOnlyCollection<TrackPoint>? trackPoints, bool mapExpected)
    {
        if (trackPoints is { Count: > 0 })
        {
            return SurfacePresentationState.Ready;
        }

        return mapExpected
            ? SurfacePresentationState.WaitingForData("Waiting for map data.")
            : SurfacePresentationState.Hidden;
    }

    private void RefreshTrackTimelineContext()
    {
        TrackTimelineContext = TelemetryData is { } telemetry
            ? TrackPointSeries.BuildTimelineContext(
                TrackPoints,
                telemetry.Metadata.Timestamp,
                telemetry.Metadata.Duration)
            : null;
    }

    private void ClearRecordedPresentation()
    {
        TelemetryData = null;
        FullTrackPoints = null;
        TrackPoints = null;
        MapVideoWidth = null;
        ClearDamperPercentages();
        HideVibrationStates();
        ApplyRecordedPlotAvailability(null);
        SetRecordedGraphBaseStates(
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
    }

    private void ApplyRecordedLoadingStates(bool mapExpected)
    {
        ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedGraphBaseStates(
            SurfacePresentationState.Loading("Loading travel graphs."),
            SurfacePresentationState.Loading("Loading velocity graph."),
            SurfacePresentationState.Loading("Loading IMU graph."),
            SurfacePresentationState.Loading("Loading pitch/roll graph."),
            mapExpected ? SurfacePresentationState.Loading("Loading speed graph.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.Loading("Loading elevation graph.") : SurfacePresentationState.Hidden);
        FrontStatisticsState = SurfacePresentationState.Loading("Loading statistics.");
        RearStatisticsState = SurfacePresentationState.Loading("Loading statistics.");
        CompressionBalanceState = SurfacePresentationState.Loading("Loading balance data.");
        ReboundBalanceState = SurfacePresentationState.Loading("Loading balance data.");
        HideVibrationStates();
        MapState = mapExpected
            ? SurfacePresentationState.Loading("Loading map data.")
            : SurfacePresentationState.Hidden;
        SpringPage.FrontHistogramState = SurfacePresentationState.Loading("Loading spring chart.");
        SpringPage.RearHistogramState = SurfacePresentationState.Loading("Loading spring chart.");
        DamperPage.FrontHistogramState = SurfacePresentationState.Loading("Loading damping chart.");
        DamperPage.RearHistogramState = SurfacePresentationState.Loading("Loading damping chart.");
        BalancePage.CompressionBalanceState = SurfacePresentationState.Loading("Loading balance chart.");
        BalancePage.ReboundBalanceState = SurfacePresentationState.Loading("Loading balance chart.");
    }

    private void ApplyRecordedWaitingStates(bool mapExpected)
    {
        ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedGraphBaseStates(
            SurfacePresentationState.WaitingForData("Waiting for travel data."),
            SurfacePresentationState.WaitingForData("Waiting for velocity data."),
            SurfacePresentationState.WaitingForData("Waiting for IMU data."),
            SurfacePresentationState.WaitingForData("Waiting for pitch/roll data."),
            mapExpected ? SurfacePresentationState.WaitingForData("Waiting for speed data.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.WaitingForData("Waiting for elevation data.") : SurfacePresentationState.Hidden);
        FrontStatisticsState = SurfacePresentationState.WaitingForData("Waiting for statistics.");
        RearStatisticsState = SurfacePresentationState.WaitingForData("Waiting for statistics.");
        CompressionBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance data.");
        ReboundBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance data.");
        HideVibrationStates();
        MapState = mapExpected
            ? SurfacePresentationState.WaitingForData("Waiting for map data.")
            : SurfacePresentationState.Hidden;
        SpringPage.FrontHistogramState = SurfacePresentationState.WaitingForData("Waiting for spring chart.");
        SpringPage.RearHistogramState = SurfacePresentationState.WaitingForData("Waiting for spring chart.");
        DamperPage.FrontHistogramState = SurfacePresentationState.WaitingForData("Waiting for damping chart.");
        DamperPage.RearHistogramState = SurfacePresentationState.WaitingForData("Waiting for damping chart.");
        BalancePage.CompressionBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance chart.");
        BalancePage.ReboundBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance chart.");
    }

    private void ApplyRecordedLoadedStates(SessionTelemetryPresentationData data)
    {
        ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedReadyGraphStates(data.TelemetryData);

        if (data.TelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
        else
        {
            FrontStatisticsState = SurfacePresentationState.Hidden;
            RearStatisticsState = SurfacePresentationState.Hidden;
            CompressionBalanceState = SurfacePresentationState.Hidden;
            ReboundBalanceState = SurfacePresentationState.Hidden;
            HideVibrationStates();
        }

        MapState = CreateMapState(data.TrackPoints, data.FullTrackId is not null);
    }

    private void ApplyDesktopLoadResult(SessionDesktopLoadResult result)
    {
        switch (result)
        {
            case SessionDesktopLoadResult.Loaded loaded:
                ApplyDampingSpeedCutoffContext(
                    loaded.Data.DampingSpeedCutoffs,
                    loaded.Data.DampingSpeedCutoffOwner);
                suppressAnalysisRecompute = true;
                try
                {
                    TelemetryData = loaded.Data.TelemetryData;
                }
                finally
                {
                    suppressAnalysisRecompute = false;
                }
                session.FullTrack = loaded.Data.FullTrackId;
                FullTrackPoints = loaded.Data.FullTrackPoints;
                TrackPoints = loaded.Data.TrackPoints;
                MapVideoWidth = loaded.Data.MapVideoWidth;
                ApplyModeAwareDamperPercentages(loaded.Data.DamperPercentages);
                ApplyRecordedLoadedStates(loaded.Data);
                RecomputeSessionAnalysis();
                lastObservedHasProcessedData = true;
                break;

            case SessionDesktopLoadResult.TelemetryPending:
                ClearRecordedPresentation();
                ApplyRecordedWaitingStates(session.FullTrack is not null);
                lastObservedHasProcessedData = false;
                break;

            case SessionDesktopLoadResult.Failed failed:
                ClearRecordedPresentation();
                ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
                break;
        }
    }

    private void ApplyMobileLoadResult(SessionMobileLoadResult result)
    {
        switch (result)
        {
            case SessionMobileLoadResult.LoadedFromCache loadedFromCache:
                ApplyCachePresentation(loadedFromCache.Data);
                suppressAnalysisRecompute = true;
                try
                {
                    TelemetryData = loadedFromCache.Telemetry;
                }
                finally
                {
                    suppressAnalysisRecompute = false;
                }
                ApplyMobileExtendedStatisticsStates(
                    loadedFromCache.Telemetry,
                    HasFrontCacheStatistics(loadedFromCache.Data),
                    HasRearCacheStatistics(loadedFromCache.Data),
                    loadedFromCache.Data.BalanceAvailable);
                ApplyRecordedReadyGraphStates(TelemetryData);
                ApplyMobileTrackPresentation(loadedFromCache.TrackData);
                ScreenState = SessionScreenPresentationState.Ready;
                IsComplete = true;
                RecomputeSessionAnalysis();
                lastObservedHasProcessedData = true;
                break;

            case SessionMobileLoadResult.BuiltCache builtCache:
                ApplyCachePresentation(builtCache.Data);
                suppressAnalysisRecompute = true;
                try
                {
                    TelemetryData = builtCache.Telemetry;
                }
                finally
                {
                    suppressAnalysisRecompute = false;
                }
                ApplyMobileExtendedStatisticsStates(
                    builtCache.Telemetry,
                    HasFrontCacheStatistics(builtCache.Data),
                    HasRearCacheStatistics(builtCache.Data),
                    builtCache.Data.BalanceAvailable);
                ApplyRecordedReadyGraphStates(TelemetryData);
                ApplyMobileTrackPresentation(builtCache.TrackData);
                ScreenState = SessionScreenPresentationState.Ready;
                IsComplete = true;
                RecomputeSessionAnalysis();
                lastObservedHasProcessedData = true;
                break;

            case SessionMobileLoadResult.TelemetryPending:
                ApplyRecordedWaitingStates(mapExpected: false);
                lastObservedHasProcessedData = false;
                break;

            case SessionMobileLoadResult.Failed failed:
                ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
                break;
        }
    }

    private static bool HasFrontCacheStatistics(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.FrontTravelHistogram)
               || !string.IsNullOrWhiteSpace(data.FrontVelocityHistogram);
    }

    private static bool HasRearCacheStatistics(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.RearTravelHistogram)
               || !string.IsNullOrWhiteSpace(data.RearVelocityHistogram);
    }

    private void ApplyMobileExtendedStatisticsStates(
        TelemetryData? telemetry,
        bool frontStatisticsAvailable,
        bool rearStatisticsAvailable,
        bool balanceAvailable)
    {
        if (telemetry is null)
        {
            FrontStatisticsState = SurfacePresentationState.Hidden;
            RearStatisticsState = SurfacePresentationState.Hidden;
            CompressionBalanceState = SurfacePresentationState.Hidden;
            ReboundBalanceState = SurfacePresentationState.Hidden;
            HideVibrationStates();
            return;
        }

        ApplyAnalysisRangeStates(telemetry);
        if (!frontStatisticsAvailable && FrontStatisticsState.Kind != SurfaceStateKind.NoData)
        {
            FrontStatisticsState = SurfacePresentationState.Hidden;
            FrontForkVibrationState = SurfacePresentationState.Hidden;
            FrontFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!rearStatisticsAvailable && RearStatisticsState.Kind != SurfaceStateKind.NoData)
        {
            RearStatisticsState = SurfacePresentationState.Hidden;
            RearForkVibrationState = SurfacePresentationState.Hidden;
            RearFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!balanceAvailable)
        {
            CompressionBalanceState = SurfacePresentationState.Hidden;
            ReboundBalanceState = SurfacePresentationState.Hidden;
        }
    }

    private void ApplyMobileTrackPresentation(SessionTrackPresentationData? trackData)
    {
        session.FullTrack = trackData?.FullTrackId;
        FullTrackPoints = trackData?.FullTrackPoints;
        TrackPoints = trackData?.TrackPoints;
        MapVideoWidth = trackData?.MapVideoWidth;
        MapState = CreateMapState(trackData?.TrackPoints, trackData?.FullTrackId is not null);
    }

    private async Task RequestLoadAsync()
    {
        if (!viewLoaded)
        {
            return;
        }

        var token = loadOperation.Start();
        var currentSnapshot = sessionStore.Get(Id);
        if (currentSnapshot?.HasProcessedData == true)
        {
            ClearRecordedPresentation();
            ApplyRecordedLoadingStates(currentSnapshot.FullTrackId is not null);
        }
        else
        {
            ScreenState = SessionScreenPresentationState.Ready;
        }

        try
        {
            if (App.Current?.IsDesktop == true)
            {
                var result = await sessionCoordinator.LoadDesktopDetailAsync(Id, token);
                if (token.IsCancellationRequested) return;
                ApplyDesktopLoadResult(result);
                return;
            }

            if (lastPresentationDimensions is null) return;

            var mobileResult = await sessionCoordinator.LoadMobileDetailAsync(
                Id, lastPresentationDimensions.Value, token);
            if (token.IsCancellationRequested) return;
            ApplyMobileLoadResult(mobileResult);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static bool ShouldPromptForDerivedChange(DerivedChangeKind changeKind) =>
        changeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged) ||
        changeKind.HasFlag(DerivedChangeKind.DependencyChanged) ||
        changeKind.HasFlag(DerivedChangeKind.SourceAvailabilityChanged) ||
        changeKind.HasFlag(DerivedChangeKind.FingerprintChanged);

    private async Task ApplyPersistedSnapshotAsync(SessionSnapshot snapshot)
    {
        session = SessionFromSnapshot(snapshot);
        SessionContext.SessionSnapshot = snapshot;
        BaselineUpdated = snapshot.Updated;
        IsComplete = snapshot.HasProcessedData;
        lastObservedHasProcessedData = snapshot.HasProcessedData;
        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
        UpdateRecordedSessionExtensionHostState();
    }

    private async Task HandleDomainChangedAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!viewLoaded)
        {
            return;
        }

        latestDomain = domain;
        UpdateRecordedSessionExtensionHostState();

        if (ShouldDeferDomainHandling())
        {
            deferredDomainWhileInactive = domain;
            return;
        }

        deferredDomainWhileInactive = null;

        var initial = !observedInitialDomain;
        observedInitialDomain = true;

        if (initial)
        {
            await HandleInitialDomainAsync(domain);
            return;
        }

        if (!domain.Staleness.IsStale)
        {
            promptedRecomputeSignature = null;
        }

        if (ShouldPromptForDerivedChange(domain.ChangeKind) && domain.Staleness.CanRecompute)
        {
            await PromptForRecomputeAsync(domain);
            return;
        }

        if (domain.Session.Updated > BaselineUpdated && !domain.Staleness.IsStale)
        {
            await ReloadFreshExternalUpdateAsync(domain);
            return;
        }

        if (domain.ChangeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged) &&
            domain.Session.HasProcessedData &&
            !domain.Staleness.IsStale)
        {
            lastObservedHasProcessedData = domain.Session.HasProcessedData;
            _ = RequestLoadAsync();
        }
    }

    private async Task HandleInitialDomainAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!domain.Staleness.IsStale)
        {
            return;
        }

        if (domain.Staleness.CanRecompute)
        {
            await PromptForRecomputeAsync(domain);
            return;
        }

        ReportNotRecomputableStale();
    }

    private async Task ReloadFreshExternalUpdateAsync(RecordedSessionDomainSnapshot domain)
    {
        if (IsDirty)
        {
            var reload = await dialogService.ShowConfirmationAsync(
                "Session changed elsewhere",
                "This session has been updated from another source. Discard your changes and reload?");
            if (!reload)
            {
                return;
            }
        }

        await ApplyPersistedSnapshotAsync(domain.Session);
        await RequestLoadAsync();
    }

    private void ReportNotRecomputableStale()
    {
        if (reportedNotRecomputableStale)
        {
            return;
        }

        reportedNotRecomputableStale = true;
        ErrorMessages.Add("Session is stale and cannot be recomputed until the source recording is restored.");
    }

    private async Task PromptForRecomputeAsync(RecordedSessionDomainSnapshot domain)
    {
        if (recomputePromptRunning)
        {
            return;
        }

        var signature = RecomputePromptSignature(domain);
        if (promptedRecomputeSignature == signature)
        {
            return;
        }

        promptedRecomputeSignature = signature;
        recomputePromptRunning = true;
        try
        {
            var confirmed = await dialogService.ShowConfirmationAsync(
                RecomputePromptTitle(domain),
                RecomputePromptMessage(IsDirty));
            if (!confirmed || !viewLoaded)
            {
                return;
            }

            await ApplyPersistedSnapshotAsync(domain.Session);
            var result = await sessionCoordinator.RecomputeAsync(Id, BaselineUpdated);
            await ApplyRecomputeResultAsync(result);
        }
        finally
        {
            recomputePromptRunning = false;
        }
    }

    private static string RecomputePromptTitle(RecordedSessionDomainSnapshot domain) =>
        string.IsNullOrWhiteSpace(domain.Session.Name)
            ? "Session has to be recomputed"
            : $"Session {domain.Session.Name} has to be recomputed";

    private static string RecomputePromptMessage(bool isDirty) =>
        isDirty
            ? "Recompute this session now? This will discard unsaved changes."
            : "Recompute this session now?";

    private static string RecomputePromptSignature(RecordedSessionDomainSnapshot domain) =>
        string.Join(
            "|",
            domain.Session.Id,
            domain.Session.Updated,
            domain.Session.ProcessingFingerprintJson,
            domain.CurrentFingerprint?.SchemaVersion,
            domain.CurrentFingerprint?.ProcessingVersion,
            domain.CurrentFingerprint?.SetupId,
            domain.CurrentFingerprint?.BikeId,
            domain.CurrentFingerprint?.DependencyHash,
            domain.CurrentFingerprint?.SourceHash,
            domain.Staleness.GetType().FullName);

    private async Task ApplyRecomputeResultAsync(SessionRecomputeResult result)
    {
        switch (result)
        {
            case SessionRecomputeResult.Recomputed recomputed:
                BaselineUpdated = recomputed.NewBaselineUpdated;
                if (sessionStore.Get(Id) is { } current)
                {
                    await ApplyPersistedSnapshotAsync(current);
                }

                await RequestLoadAsync();
                break;

            case SessionRecomputeResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Session changed elsewhere",
                    "This session has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    await ApplyPersistedSnapshotAsync(conflict.CurrentSnapshot);
                    await RequestLoadAsync();
                }
                break;

            case SessionRecomputeResult.NotRecomputable:
                ReportNotRecomputableStale();
                break;

            case SessionRecomputeResult.Failed failed:
                ErrorMessages.Add($"Session could not be recomputed: {failed.ErrorMessage}");
                break;
        }
    }

    private RecordedSessionHostState CreateRecordedSessionExtensionHostState()
    {
        var snapshot = sessionStore.Get(Id);
        var timelineDurationSeconds = TelemetryData?.Metadata.Duration ?? snapshot?.DurationSeconds;

        return new RecordedSessionHostState(
            new RecordedSessionIdentityState(
                Id,
                snapshot?.Name,
                snapshot?.Timestamp,
                snapshot?.DurationSeconds,
                viewLoaded,
                IsTabActive),
            new RecordedSessionSelectionState(AnalysisRange),
            new RecordedSessionTimelineState(
                TrackTimelineContext,
                timelineDurationSeconds,
                Timeline),
            new RecordedSessionStatisticsState(
                DamperPercentages,
                DampingSpeedCutoffs,
                SelectedVelocityAverageMode,
                SelectedTravelHistogramMode));
    }

    private void UpdateRecordedSessionExtensionHostState()
    {
        recordedSessionExtensions?.UpdateHostState(CreateRecordedSessionExtensionHostState());
    }

    private async ValueTask InitializeRecordedSessionExtensionsAsync(CancellationToken cancellationToken = default)
    {
        if (recordedSessionExtensions is null)
        {
            return;
        }

        await recordedSessionExtensions.InitializeAsync(
            CreateRecordedSessionExtensionHostState(),
            cancellationToken);
    }

    private async ValueTask DisposeRecordedSessionExtensionScopesAsync()
    {
        if (recordedSessionExtensions is null)
        {
            return;
        }

        await recordedSessionExtensions.DisposeScopesAsync();
    }

    private void ReportRecordedSessionExtensionOperation(string message, double percent)
    {
        SessionOperationState = SessionOperationPresentationState.Progress(message, percent);
    }

    private void CompleteRecordedSessionExtensionOperation()
    {
        SessionOperationState = SessionOperationPresentationState.Hidden;
    }

    private void SetRecordedSessionExtensionTimelineVisibleRange(
        double startNormalized,
        double endNormalized,
        object source)
    {
        Timeline.SetVisibleRange(startNormalized, endNormalized, source);
    }

    private void RequestRecordedSessionExtensionPageSelection(string contributionId)
    {
        if (recordedSessionExtensions is null)
        {
            return;
        }

        var contribution = recordedSessionExtensions.ExtensionSlots.Pages
            .Where(contribution => StringComparer.Ordinal.Equals(contribution.ContributionId, contributionId))
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (contribution is null ||
            !recordedSessionExtensionPages.TryGetValue(RecordedSessionExtensionPageKey(contribution), out var page))
        {
            return;
        }

        foreach (var currentPage in Pages)
        {
            currentPage.Selected = false;
        }

        page.Selected = true;
    }

    private void OnRecordedSessionExtensionPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ApplyRecordedSessionExtensionPages();
    }

    private void OnRecordedSessionExtensionMediaPanesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasMediaContent));
    }

    private void ApplyRecordedSessionExtensionPages()
    {
        if (recordedSessionExtensions is null)
        {
            return;
        }

        var contributions = recordedSessionExtensions.ExtensionSlots.Pages
            .OrderBy(contribution => contribution.RequestedIndex)
            .ThenBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ContributionId, StringComparer.Ordinal)
            .ToArray();
        var desiredKeys = contributions
            .Select(RecordedSessionExtensionPageKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in recordedSessionExtensionPages.ToArray())
        {
            Pages.Remove(entry.Value);
            if (!desiredKeys.Contains(entry.Key))
            {
                recordedSessionExtensionPages.Remove(entry.Key);
            }
        }

        var insertedCount = 0;
        foreach (var contribution in contributions)
        {
            var key = RecordedSessionExtensionPageKey(contribution);
            if (!recordedSessionExtensionPages.TryGetValue(key, out var page))
            {
                page = new RecordedSessionExtensionPageViewModel(
                    contribution.DisplayName,
                    contribution.ViewModel);
                recordedSessionExtensionPages.Add(key, page);
            }

            var insertIndex = Math.Clamp(contribution.RequestedIndex + insertedCount, 0, Pages.Count);
            Pages.Insert(insertIndex, page);
            insertedCount++;
        }
    }

    private static string RecordedSessionExtensionPageKey(RecordedSessionPageContribution contribution)
    {
        return $"{contribution.ExtensionId}\u001f{contribution.ContributionId}";
    }

    #endregion

    #region Constructors

    internal SessionDetailViewModel(
        SessionSnapshot snapshot,
        ISessionCoordinator sessionCoordinator,
        ISessionStore sessionStore,
        IRecordedSessionGraph recordedSessionGraph,
        ISessionPresentationService sessionPresentationService,
        ISessionAnalysisService sessionAnalysisService,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISessionPreferences sessionPreferences,
        IUiThreadDispatcher uiThreadDispatcher,
        IBikeCoordinator? bikeCoordinator = null,
        IEnumerable<IRecordedSessionExtensionFactory>? recordedSessionExtensionFactories = null,
        IExtensionDatabaseConnection? extensionDatabase = null,
        IRecordedSessionDataReader? recordedSessionDataReader = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null)
        : base(shell, dialogService, uiThreadDispatcher)
    {
        ArgumentNullException.ThrowIfNull(sessionPreferences);

        var extensionFactories = recordedSessionExtensionFactories?.ToArray() ?? [];
        if (extensionFactories.Length > 0 &&
            (extensionDatabase is null || recordedSessionDataReader is null || backgroundTaskRunner is null))
        {
            throw new ArgumentException("Recorded-session extension factories require extension host services.");
        }

        this.sessionCoordinator = sessionCoordinator;
        this.bikeCoordinator = bikeCoordinator;
        this.sessionStore = sessionStore;
        this.recordedSessionGraph = recordedSessionGraph;
        this.sessionPresentationService = sessionPresentationService;
        this.sessionAnalysisService = sessionAnalysisService;
        this.sessionPreferences = sessionPreferences;
        showAirtimeAction = CreateAirtimeAction("travel_airtime", ShowAirtime, () => ShowAirtime = !ShowAirtime);
        showVelocityAirtimeAction = CreateAirtimeAction("velocity_airtime", ShowVelocityAirtime, () => ShowVelocityAirtime = !ShowVelocityAirtime);
        showImuAirtimeAction = CreateAirtimeAction("imu_airtime", ShowImuAirtime, () => ShowImuAirtime = !ShowImuAirtime);
        showPitchRollAirtimeAction = CreateAirtimeAction("pitch_roll_airtime", ShowPitchRollAirtime, () => ShowPitchRollAirtime = !ShowPitchRollAirtime);
        showSpeedAirtimeAction = CreateAirtimeAction("speed_airtime", ShowSpeedAirtime, () => ShowSpeedAirtime = !ShowSpeedAirtime);
        showElevationAirtimeAction = CreateAirtimeAction("elevation_airtime", ShowElevationAirtime, () => ShowElevationAirtime = !ShowElevationAirtime);
        showStatisticsSelectionAction = CreateStatisticsSelectionAction("travel_statistics_selection", ShowStatisticsSelection, () => ShowStatisticsSelection = !ShowStatisticsSelection);
        showVelocityStatisticsSelectionAction = CreateStatisticsSelectionAction("velocity_statistics_selection", ShowVelocityStatisticsSelection, () => ShowVelocityStatisticsSelection = !ShowVelocityStatisticsSelection);
        showImuStatisticsSelectionAction = CreateStatisticsSelectionAction("imu_statistics_selection", ShowImuStatisticsSelection, () => ShowImuStatisticsSelection = !ShowImuStatisticsSelection);
        showPitchRollStatisticsSelectionAction = CreateStatisticsSelectionAction("pitch_roll_statistics_selection", ShowPitchRollStatisticsSelection, () => ShowPitchRollStatisticsSelection = !ShowPitchRollStatisticsSelection);
        showSpeedStatisticsSelectionAction = CreateStatisticsSelectionAction("speed_statistics_selection", ShowSpeedStatisticsSelection, () => ShowSpeedStatisticsSelection = !ShowSpeedStatisticsSelection);
        showElevationStatisticsSelectionAction = CreateStatisticsSelectionAction("elevation_statistics_selection", ShowElevationStatisticsSelection, () => ShowElevationStatisticsSelection = !ShowElevationStatisticsSelection);
        TravelHeaderActions = [showAirtimeAction, showStatisticsSelectionAction];
        VelocityHeaderActions = [showVelocityAirtimeAction, showVelocityStatisticsSelectionAction];
        ImuHeaderActions = [showImuAirtimeAction, showImuStatisticsSelectionAction];
        PitchRollHeaderActions = [showPitchRollAirtimeAction, showPitchRollStatisticsSelectionAction];
        SpeedHeaderActions = [showSpeedAirtimeAction, showSpeedStatisticsSelectionAction];
        ElevationHeaderActions = [showElevationAirtimeAction, showElevationStatisticsSelectionAction];
        plotAutozoomController = new PlotAutozoomController(Timeline);
        PlotContextMenuActionsByRowId = plotAutozoomController.ActionsByRowId;
        SessionContext.TravelHeaderActions = TravelHeaderActions;
        SessionContext.VelocityHeaderActions = VelocityHeaderActions;
        SessionContext.ImuHeaderActions = ImuHeaderActions;
        SessionContext.PitchRollHeaderActions = PitchRollHeaderActions;
        SessionContext.SpeedHeaderActions = SpeedHeaderActions;
        SessionContext.ElevationHeaderActions = ElevationHeaderActions;
        SessionContext.PlotContextMenuActionsByRowId = PlotContextMenuActionsByRowId;
        session = SessionFromSnapshot(snapshot);
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        SessionContext.SessionSnapshot = snapshot;
        MobileWorkspace = new SessionShellMobileWorkspaceViewModel(SessionContext);
        GraphWorkspace = new RecordedSessionGraphWorkspaceViewModel(
            SessionContext,
            value => GraphPreferences = value,
            SetAnalysisRange,
            ClearAnalysisRange,
            SetAnalysisRangeBoundary);
        MediaWorkspace = new SessionMediaWorkspaceViewModel(SessionContext);
        StatisticsWorkspace = new SessionStatisticsWorkspaceViewModel(
            SessionContext,
            value => SelectedTravelHistogramMode = value,
            value => SelectedBalanceDisplacementMode = value,
            value => SelectedBalanceSpeedMode = value,
            value => SelectedVelocityAverageMode = value,
            value => SelectedSessionAnalysisTargetProfile = value,
            SelectTelemetryRangeSelectionCommand,
            PreviewDampingSpeedCutoff,
            CancelDampingSpeedCutoffPreview,
            CommitDampingSpeedCutoffAsync);
        SidebarWorkspace = new SessionSidebarWorkspaceViewModel(
            this,
            () => Name,
            value => Name = value,
            NotesPage,
            PreferencesPage,
            SaveCommand,
            ResetCommand);
        IsComplete = snapshot.HasProcessedData;
        lastObservedHasProcessedData = snapshot.HasProcessedData;
        if (extensionDatabase is not null && recordedSessionDataReader is not null && backgroundTaskRunner is not null)
        {
            recordedSessionExtensions = new RecordedSessionExtensionManager(
                Id,
                extensionFactories,
                extensionDatabase,
                recordedSessionDataReader,
                backgroundTaskRunner,
                uiThreadDispatcher,
                new RecordedSessionOperationCoordinator(
                    ReportRecordedSessionExtensionOperation,
                    CompleteRecordedSessionExtensionOperation),
                SetAnalysisRange,
                ClearAnalysisRange,
                SetRecordedSessionExtensionTimelineVisibleRange,
                ErrorMessages.Add,
                Notifications.Add,
                RequestRecordedSessionExtensionPageSelection);
            recordedSessionExtensions.ExtensionSlots.Pages.CollectionChanged += OnRecordedSessionExtensionPagesChanged;
            recordedSessionExtensions.ExtensionSlots.MediaPanes.CollectionChanged += OnRecordedSessionExtensionMediaPanesChanged;
        }
        SessionContext.ExtensionSlots = ExtensionSlots;

        GraphPage = new RecordedGraphPageViewModel(GraphWorkspace, MediaWorkspace);
        SpringPage = new SpringPageViewModel(StatisticsWorkspace);
        StrokesPage = new StrokesPageViewModel(StatisticsWorkspace);
        DamperPage = new DamperPageViewModel(StatisticsWorkspace);
        BalancePage = new BalancePageViewModel(StatisticsWorkspace);
        VibrationPage = new VibrationPageViewModel(StatisticsWorkspace);
        AnalysisPage = new SessionAnalysisPageViewModel(StatisticsWorkspace);
        Pages.Add(GraphPage);
        Pages.Add(SpringPage);
        Pages.Add(StrokesPage);
        Pages.Add(DamperPage);
        Pages.Add(BalancePage);
        Pages.Add(VibrationPage);
        Pages.Add(AnalysisPage);
        Pages.Add(NotesPage);
        Pages.Add(PreferencesPage);
        SessionContext.MapViewModel = new MapViewModel(tileLayerService, dialogService, uiThreadDispatcher);
        _ = SessionContext.MapViewModel.InitializeAsync();
        if (snapshot.HasProcessedData)
        {
            ApplyRecordedLoadingStates(snapshot.FullTrackId is not null);
        }

        NotesPage.ForkSettings.PropertyChanged += (_, _) => EvaluateDirtinessFromPageChange();
        NotesPage.ShockSettings.PropertyChanged += (_, _) => EvaluateDirtinessFromPageChange();
        NotesPage.PropertyChanged += (_, _) => EvaluateDirtinessFromPageChange();
        PreferencesPage.TravelPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.VelocityPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.ImuPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.PitchRollPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.SpeedPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.ElevationPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.ProcessingPreferenceChangeCommitted += OnProcessingPreferenceChangeCommitted;

        ResetImplementation();
    }

    #endregion

    #region Private methods

    private static Session SessionFromSnapshot(SessionSnapshot snapshot)
    {
        var s = new Session(snapshot.Id, snapshot.Name, snapshot.Description, snapshot.SetupId, snapshot.Timestamp)
        {
            FullTrack = snapshot.FullTrackId,
            FrontSpringRate = snapshot.FrontSpringRate,
            FrontHighSpeedCompression = snapshot.FrontHighSpeedCompression,
            FrontLowSpeedCompression = snapshot.FrontLowSpeedCompression,
            FrontLowSpeedRebound = snapshot.FrontLowSpeedRebound,
            FrontHighSpeedRebound = snapshot.FrontHighSpeedRebound,
            RearSpringRate = snapshot.RearSpringRate,
            RearHighSpeedCompression = snapshot.RearHighSpeedCompression,
            RearLowSpeedCompression = snapshot.RearLowSpeedCompression,
            RearLowSpeedRebound = snapshot.RearLowSpeedRebound,
            RearHighSpeedRebound = snapshot.RearHighSpeedRebound,
            HasProcessedData = snapshot.HasProcessedData,
            Updated = snapshot.Updated,
        };
        return s;
    }

    private TelemetryPlotRowAction CreateAirtimeAction(string id, bool isChecked, Action toggle)
    {
        return new TelemetryPlotRowAction
        {
            Id = id,
            Kind = TelemetryPlotRowActionKind.Toggle,
            IconGeometry = Geometry.Parse(
                "M12 4C7 4 3 7 1 12C3 17 7 20 12 20C17 20 21 17 23 12C21 7 17 4 12 4ZM12 16C9.8 16 8 14.2 8 12C8 9.8 9.8 8 12 8C14.2 8 16 9.8 16 12C16 14.2 14.2 16 12 16Z"),
            ToolTip = isChecked ? "Hide airtime" : "Show airtime",
            Command = new RelayCommand(toggle),
            IsChecked = isChecked,
            Tone = TelemetryPlotRowActionTone.Default,
        };
    }

    private TelemetryPlotRowAction CreateStatisticsSelectionAction(string id, bool isChecked, Action toggle)
    {
        var action = new TelemetryPlotRowAction
        {
            Id = id,
            Kind = TelemetryPlotRowActionKind.Toggle,
            IconGeometry = Geometry.Parse("M4 6H20V8H4V6ZM4 11H17V13H4V11ZM4 16H13V18H4V16Z"),
            Command = new RelayCommand(toggle),
            Tone = TelemetryPlotRowActionTone.Default,
        };
        UpdateStatisticsSelectionAction(action, isChecked, HasStatisticsSelection);
        return action;
    }

    private static void UpdateAirtimeAction(TelemetryPlotRowAction action, bool isChecked)
    {
        action.IsChecked = isChecked;
        action.ToolTip = isChecked ? "Hide airtime" : "Show airtime";
    }

    private static void UpdateStatisticsSelectionAction(TelemetryPlotRowAction action, bool isChecked, bool hasSelection)
    {
        action.IsChecked = isChecked;
        action.IsEnabled = hasSelection;
        action.ToolTip = hasSelection
            ? isChecked ? "Hide selected strokes" : "Show selected strokes"
            : "Select a stroke group";
    }

    private void ClearStatisticsSelections()
    {
        SetSelectedFrontRangeSelection(null);
        SetSelectedRearRangeSelection(null);
        StatisticsSelectionHighlightRanges = [];
        ClearStatisticsSelectionToggles();
        RefreshStatisticsSelectionActionStates();
    }

    private void ClearDampingRangeSelections()
    {
        var changed = false;
        if (frontTelemetryRangeSelection is DampingRangeSelection)
        {
            SetSelectedFrontRangeSelection(null);
            changed = true;
        }

        if (rearTelemetryRangeSelection is DampingRangeSelection)
        {
            SetSelectedRearRangeSelection(null);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        RecomputeStatisticsSelectionHighlightRanges();
        if (!HasStatisticsSelection)
        {
            ClearStatisticsSelectionToggles();
        }

        RefreshStatisticsSelectionActionStates();
    }

    private void ClearSelectionsFromOtherStatistics(TelemetryRangeSelection selection)
    {
        var clearDampingSelections = IsStrokeStatisticsSelection(selection);
        var clearStrokeSelections = selection is DampingRangeSelection;
        if (!clearDampingSelections && !clearStrokeSelections)
        {
            return;
        }

        if (ShouldClearStatisticsSelection(frontTelemetryRangeSelection, clearDampingSelections, clearStrokeSelections))
        {
            SetSelectedFrontRangeSelection(null);
        }

        if (ShouldClearStatisticsSelection(rearTelemetryRangeSelection, clearDampingSelections, clearStrokeSelections))
        {
            SetSelectedRearRangeSelection(null);
        }
    }

    private void SetSelectedFrontRangeSelection(TelemetryRangeSelection? selection)
    {
        if (frontTelemetryRangeSelection == selection)
        {
            return;
        }

        frontTelemetryRangeSelection = selection;
        SessionContext.SelectedFrontRangeSelection = selection;
        OnPropertyChanged(nameof(SelectedFrontRangeSelection));
    }

    private void SetSelectedRearRangeSelection(TelemetryRangeSelection? selection)
    {
        if (rearTelemetryRangeSelection == selection)
        {
            return;
        }

        rearTelemetryRangeSelection = selection;
        SessionContext.SelectedRearRangeSelection = selection;
        OnPropertyChanged(nameof(SelectedRearRangeSelection));
    }

    private static bool ShouldClearStatisticsSelection(
        TelemetryRangeSelection? selection,
        bool clearDampingSelections,
        bool clearStrokeSelections)
    {
        return (clearDampingSelections && selection is DampingRangeSelection) ||
               (clearStrokeSelections && IsStrokeStatisticsSelection(selection));
    }

    private static bool IsStrokeStatisticsSelection(TelemetryRangeSelection? selection)
    {
        return selection is StrokeLengthRangeSelection or StrokeSpeedRangeSelection or DeepTravelRangeSelection;
    }

    private void RecomputeStatisticsSelectionHighlightRanges()
    {
        if (TelemetryData is not { } telemetryData)
        {
            StatisticsSelectionHighlightRanges = [];
            RefreshStatisticsSelectionActionStates();
            return;
        }

        var ranges = new List<TelemetryHighlightRange>();
        if (frontTelemetryRangeSelection is { } frontSelection)
        {
            ranges.AddRange(CreateStatisticsHighlightRanges(telemetryData, frontSelection, AnalysisRange));
        }

        if (rearTelemetryRangeSelection is { } rearSelection)
        {
            ranges.AddRange(CreateStatisticsHighlightRanges(telemetryData, rearSelection, AnalysisRange));
        }

        StatisticsSelectionHighlightRanges = TelemetryStatistics.MergeHighlightRanges(ranges);
        RefreshStatisticsSelectionActionStates();
    }

    private static IEnumerable<TelemetryHighlightRange> CreateStatisticsHighlightRanges(
        TelemetryData telemetryData,
        TelemetryRangeSelection selection,
        TelemetryTimeRange? analysisRange)
    {
        var ranges = TelemetryStatistics.CalculateHighlightRanges(telemetryData, selection, analysisRange);
        return ranges.Select(range => range with { SuspensionType = selection.SuspensionType });
    }

    private void RefreshStatisticsSelectionActionStates()
    {
        var hasSelection = HasStatisticsSelection;
        UpdateStatisticsSelectionAction(showStatisticsSelectionAction, ShowStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showVelocityStatisticsSelectionAction, ShowVelocityStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showImuStatisticsSelectionAction, ShowImuStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showPitchRollStatisticsSelectionAction, ShowPitchRollStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showSpeedStatisticsSelectionAction, ShowSpeedStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showElevationStatisticsSelectionAction, ShowElevationStatisticsSelection, hasSelection);
    }

    private void ClearStatisticsSelectionToggles()
    {
        ShowStatisticsSelection = false;
        ShowVelocityStatisticsSelection = false;
        ShowImuStatisticsSelection = false;
        ShowPitchRollStatisticsSelection = false;
        ShowSpeedStatisticsSelection = false;
        ShowElevationStatisticsSelection = false;
    }

    private void EvaluateDirtinessFromPageChange()
    {
        if (suppressDirtinessEvaluation)
        {
            return;
        }

        EvaluateDirtiness();
    }

    private void ApplyRecordedPlotAvailability(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasFramePitchRollTelemetry = HasFramePitchRollTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(TrackPoints);
        PreferencesPage.ApplyPlotAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasImuTelemetry,
            hasFramePitchRollTelemetry,
            hasSpeedSeries,
            hasElevationSeries);
    }

    private void ApplyRecordedReadyGraphStates(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasFramePitchRollTelemetry = HasFramePitchRollTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(TrackPoints);

        PreferencesPage.ApplyPlotAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasImuTelemetry,
            hasFramePitchRollTelemetry,
            hasSpeedSeries,
            hasElevationSeries);

        var travelState = hasTravelTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var imuState = hasImuTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var pitchRollState = hasFramePitchRollTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var speedState = hasSpeedSeries
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var elevationState = hasElevationSeries
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        SetRecordedGraphBaseStates(travelState, travelState, imuState, pitchRollState, speedState, elevationState);
    }

    private void ApplyRecordedTrackGraphStates()
    {
        ApplyRecordedPlotAvailability(TelemetryData);
        recordedSpeedGraphBaseState = TrackPointSeries.HasSpeedSeries(TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        recordedElevationGraphBaseState = TrackPointSeries.HasElevationSeries(TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        RefreshRecordedGraphStates();
    }

    private void SetRecordedGraphBaseStates(
        SurfacePresentationState travelState,
        SurfacePresentationState velocityState,
        SurfacePresentationState imuState,
        SurfacePresentationState pitchRollState,
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        recordedTravelGraphBaseState = travelState;
        recordedVelocityGraphBaseState = velocityState;
        recordedImuGraphBaseState = imuState;
        recordedPitchRollGraphBaseState = pitchRollState;
        recordedSpeedGraphBaseState = speedState;
        recordedElevationGraphBaseState = elevationState;
        RefreshRecordedGraphStates();
    }

    private void RefreshRecordedGraphStates()
    {
        TravelGraphState = recordedTravelGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Travel);
        VelocityGraphState = recordedVelocityGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Velocity);
        ImuGraphState = recordedImuGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Imu);
        PitchRollGraphState = recordedPitchRollGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.PitchRoll);
        SpeedGraphState = recordedSpeedGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Speed);
        ElevationGraphState = recordedElevationGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Elevation);
    }

    private async Task RestoreRecordedPreferencesAsync()
    {
        recordedPreferencePersistenceEnabled = false;
        try
        {
            ApplyRecordedPreferences(await sessionPreferences.GetRecordedAsync(Id));
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Session preferences could not be loaded: {e.Message}");
            ApplyRecordedPreferences(SessionPreferences.Default);
        }
        finally
        {
            recordedPreferencePersistenceEnabled = true;
        }
    }

    private void ApplyRecordedPreferences(SessionPreferences preferences)
    {
        recordedPreferences = preferences;
        PlotPreferences = preferences.Plots;
        GraphPreferences = preferences.Graph;
        PreferencesPage.ApplyPlotPreferences(preferences.Plots);
        PreferencesPage.ApplyProcessingPreferences(preferences.Processing);
        ApplyRecordedStatisticsPreferences(preferences.Statistics);
        RefreshRecordedGraphStates();
    }

    private void ApplyRecordedStatisticsPreferences(SessionStatisticsPreferences preferences)
    {
        suppressAnalysisRecompute = true;
        try
        {
            SelectedTravelHistogramMode = preferences.TravelHistogramMode;
            SelectedVelocityAverageMode = preferences.VelocityAverageMode;
            SelectedBalanceDisplacementMode = preferences.BalanceDisplacementMode;
            SelectedBalanceSpeedMode = preferences.BalanceSpeedMode;
            SelectedSessionAnalysisTargetProfile = preferences.SessionAnalysisTargetProfile;
        }
        finally
        {
            suppressAnalysisRecompute = false;
        }
    }

    private void OnPlotPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(PlotPreferenceItemViewModel.Selected) or nameof(PlotPreferenceItemViewModel.SelectedSmoothing)))
        {
            return;
        }

        var plots = PreferencesPage.CreatePlotPreferences();
        PlotPreferences = plots;
        recordedPreferences = recordedPreferences with { Plots = plots };
        RefreshRecordedGraphStates();
        PersistRecordedPreferenceChangeIfEnabled(current => current with { Plots = plots });
    }

    private SessionStatisticsPreferences CreateStatisticsPreferences()
    {
        return new SessionStatisticsPreferences(
            SelectedTravelHistogramMode,
            SelectedVelocityAverageMode,
            SelectedBalanceDisplacementMode,
            SelectedBalanceSpeedMode,
            SelectedSessionAnalysisTargetProfile);
    }

    private void PersistRecordedStatisticsPreferencesIfEnabled()
    {
        var statistics = CreateStatisticsPreferences();
        recordedPreferences = recordedPreferences with { Statistics = statistics };
        PersistRecordedPreferenceChangeIfEnabled(current => current with { Statistics = statistics });
    }

    private void OnProcessingPreferenceChangeCommitted(object? sender, EventArgs args)
    {
        _ = PersistRecordedProcessingPreferenceAndRecomputeAsync();
    }

    private async Task PersistRecordedProcessingPreferenceAndRecomputeAsync()
    {
        if (!recordedPreferencePersistenceEnabled || !viewLoaded)
        {
            return;
        }

        if (processingPreferenceRecomputeRunning)
        {
            PreferencesPage.ApplyProcessingPreferences(recordedPreferences.Processing);
            return;
        }

        var processing = PreferencesPage.CreateProcessingPreferences();
        if (processing == recordedPreferences.Processing)
        {
            return;
        }

        processingPreferenceRecomputeRunning = true;
        try
        {
            if (IsDirty)
            {
                var confirmed = await dialogService.ShowConfirmationAsync(
                    "Recompute session?",
                    "Changing the velocity filter recomputes this session and will discard unsaved changes.");
                if (!confirmed)
                {
                    PreferencesPage.ApplyProcessingPreferences(recordedPreferences.Processing);
                    return;
                }

                if (sessionStore.Get(Id) is { } current)
                {
                    await ApplyPersistedSnapshotAsync(current);
                }
            }

            var previousProcessing = recordedPreferences.Processing;
            recordedPreferences = recordedPreferences with { Processing = processing };
            try
            {
                await sessionPreferences.UpdateRecordedAsync(Id, current => current with { Processing = processing });
            }
            catch (Exception e)
            {
                recordedPreferences = recordedPreferences with { Processing = previousProcessing };
                PreferencesPage.ApplyProcessingPreferences(recordedPreferences.Processing);
                ErrorMessages.Add($"Session preferences could not be saved: {e.Message}");
                return;
            }

            var result = await sessionCoordinator.RecomputeAsync(Id, BaselineUpdated);
            await ApplyRecomputeResultAsync(result);
        }
        finally
        {
            processingPreferenceRecomputeRunning = false;
        }
    }

    private void PersistRecordedPreferenceChangeIfEnabled(Func<SessionPreferences, SessionPreferences> update)
    {
        if (!recordedPreferencePersistenceEnabled)
        {
            return;
        }

        _ = PersistRecordedPreferenceChangeAsync(update);
    }

    private async Task PersistRecordedPreferenceChangeAsync(Func<SessionPreferences, SessionPreferences> update)
    {
        try
        {
            await sessionPreferences.UpdateRecordedAsync(Id, update);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Session preferences could not be saved: {e.Message}");
        }
    }

    #endregion Private methods

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            Name != session.Name ||
            NotesPage.IsDirty(session);
    }

    protected override async Task SaveImplementation()
    {
        var newSession = new Session(
            id: session.Id,
            name: Name ?? $"session #{session.Id}",
            description: NotesPage.Description ?? $"session #{session.Id}",
            setup: session.Setup,
            timestamp: session.Timestamp)
        {
            FrontSpringRate = NotesPage.ForkSettings.SpringRate,
            FrontHighSpeedCompression = NotesPage.ForkSettings.HighSpeedCompression,
            FrontLowSpeedCompression = NotesPage.ForkSettings.LowSpeedCompression,
            FrontLowSpeedRebound = NotesPage.ForkSettings.LowSpeedRebound,
            FrontHighSpeedRebound = NotesPage.ForkSettings.HighSpeedRebound,
            RearSpringRate = NotesPage.ShockSettings.SpringRate,
            RearHighSpeedCompression = NotesPage.ShockSettings.HighSpeedCompression,
            RearLowSpeedCompression = NotesPage.ShockSettings.LowSpeedCompression,
            RearLowSpeedRebound = NotesPage.ShockSettings.LowSpeedRebound,
            RearHighSpeedRebound = NotesPage.ShockSettings.HighSpeedRebound,
            HasProcessedData = IsComplete,
            FullTrack = session.FullTrack,
        };

        var result = await sessionCoordinator.SaveAsync(newSession, BaselineUpdated);
        switch (result)
        {
            case SessionSaveResult.Saved saved:
                session = newSession;
                session.Updated = saved.NewBaselineUpdated;
                BaselineUpdated = saved.NewBaselineUpdated;
                IsDirty = false;
                break;

            case SessionSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Session changed elsewhere",
                    "This session has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    session = SessionFromSnapshot(conflict.CurrentSnapshot);
                    SessionContext.SessionSnapshot = conflict.CurrentSnapshot;
                    BaselineUpdated = conflict.CurrentSnapshot.Updated;
                    IsComplete = conflict.CurrentSnapshot.HasProcessedData;
                    lastObservedHasProcessedData = conflict.CurrentSnapshot.HasProcessedData;
                    await ResetImplementation();
                    EvaluateDirtiness();
                }
                break;

            case SessionSaveResult.Failed failed:
                ErrorMessages.Add($"Session could not be saved: {failed.ErrorMessage}");
                break;
        }
    }

    protected override Task ResetImplementation()
    {
        suppressDirtinessEvaluation = true;
        try
        {
            Id = session.Id;
            Name = session.Name;

            NotesPage.Description = session.Description;
            NotesPage.ForkSettings.SpringRate = session.FrontSpringRate;
            NotesPage.ForkSettings.HighSpeedCompression = session.FrontHighSpeedCompression;
            NotesPage.ForkSettings.LowSpeedCompression = session.FrontLowSpeedCompression;
            NotesPage.ForkSettings.LowSpeedRebound = session.FrontLowSpeedRebound;
            NotesPage.ForkSettings.HighSpeedRebound = session.FrontHighSpeedRebound;

            NotesPage.ShockSettings.SpringRate = session.RearSpringRate;
            NotesPage.ShockSettings.HighSpeedCompression = session.RearHighSpeedCompression;
            NotesPage.ShockSettings.LowSpeedCompression = session.RearLowSpeedCompression;
            NotesPage.ShockSettings.LowSpeedRebound = session.RearLowSpeedRebound;
            NotesPage.ShockSettings.HighSpeedRebound = session.RearHighSpeedRebound;
        }
        finally
        {
            suppressDirtinessEvaluation = false;
        }

        EvaluateDirtiness();

        Timestamp = DateTimeOffset.FromUnixTimeSeconds(session.Timestamp ?? 0).LocalDateTime;

        return Task.CompletedTask;
    }

    protected override async Task CloseImplementation()
    {
        await StopLoadedSessionAsync();
        MapViewModel?.Dispose();
    }

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        var result = await sessionCoordinator.DeleteAsync(Id);
        switch (result.Outcome)
        {
            case SessionDeleteOutcome.Deleted:
                if (navigateBack) OpenPreviousPage();
                break;
            case SessionDeleteOutcome.Failed:
                ErrorMessages.Add($"Session could not be deleted: {result.ErrorMessage}");
                break;
        }
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private void SelectTelemetryRangeSelection(TelemetryRangeSelection? selection)
    {
        if (selection is null || TelemetryData is null)
        {
            return;
        }

        var isClearingSelection = selection.SuspensionType == SuspensionType.Front
            ? frontTelemetryRangeSelection == selection
            : rearTelemetryRangeSelection == selection;

        if (selection.SuspensionType == SuspensionType.Front)
        {
            SetSelectedFrontRangeSelection(isClearingSelection ? null : selection);
        }
        else
        {
            SetSelectedRearRangeSelection(isClearingSelection ? null : selection);
        }

        if (!isClearingSelection)
        {
            ClearSelectionsFromOtherStatistics(selection);
        }

        RecomputeStatisticsSelectionHighlightRanges();
        ClearStatisticsSelectionToggles();
        if (HasStatisticsSelection)
        {
            ShowStatisticsSelection = true;
        }

        RefreshStatisticsSelectionActionStates();
    }

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        pendingAnalysisRangeBoundary = null;
        if (TelemetryData is null ||
            !TelemetryTimeRange.TryCreateClamped(
                startSeconds,
                endSeconds,
                TelemetryData.Metadata.Duration,
                out var range))
        {
            return;
        }

        AnalysisRange = range;
    }

    public void ClearAnalysisRange()
    {
        pendingAnalysisRangeBoundary = null;
        if (AnalysisRange is null)
        {
            return;
        }

        AnalysisRange = null;
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        if (TelemetryData is null ||
            !TelemetryTimeRange.TryClampBoundary(boundarySeconds, TelemetryData.Metadata.Duration, out var clampedBoundarySeconds))
        {
            pendingAnalysisRangeBoundary = null;
            return;
        }

        if (AnalysisRange is { } range)
        {
            if (Math.Abs(clampedBoundarySeconds - range.StartSeconds) <= Math.Abs(clampedBoundarySeconds - range.EndSeconds))
            {
                SetAnalysisRange(clampedBoundarySeconds, range.EndSeconds);
            }
            else
            {
                SetAnalysisRange(range.StartSeconds, clampedBoundarySeconds);
            }

            return;
        }

        if (pendingAnalysisRangeBoundary is not { } pendingBoundary)
        {
            pendingAnalysisRangeBoundary = clampedBoundarySeconds;
            return;
        }

        SetAnalysisRange(pendingBoundary, clampedBoundarySeconds);
    }

    public void SetAnalysisRangeBoundaryFromMarker(double markerSeconds)
    {
        SetAnalysisRangeBoundary(markerSeconds);
    }

    [RelayCommand]
    private async Task Loaded(Rect? bounds = null)
    {
        viewLoaded = true;
        var dimensions = CreatePresentationDimensions(bounds);
        if (dimensions is not null)
        {
            lastPresentationDimensions = dimensions;
        }

        // Subscribe before the awaited restore so a remote sync apply that
        // lands while restore is in flight is not missed.
        var watch = recordedSessionGraph.WatchSession(Id);
        if (SynchronizationContext.Current is { } synchronizationContext)
        {
            watch = watch.ObserveOn(synchronizationContext);
        }
        EnsureScopedSubscription(s =>
        {
            s.Add(sessionPreferences.ObserveRecorded(Id).Subscribe(OnSyncedPreferencesArrived));
            s.Add(watch.Subscribe(domain => _ = HandleDomainChangedAsync(domain)));
        });

        await InitializeRecordedSessionExtensionsAsync();
        await RestoreRecordedPreferencesAsync();
        await RequestLoadAsync();
    }

    protected override void OnActivated()
    {
        hasBeenActivated = true;
        UpdateRecordedSessionExtensionHostState();

        if (!viewLoaded || deferredDomainWhileInactive is null)
        {
            return;
        }

        var domain = deferredDomainWhileInactive;
        deferredDomainWhileInactive = null;
        _ = HandleDomainChangedAsync(domain);
    }

    protected override void OnDeactivated()
    {
        UpdateRecordedSessionExtensionHostState();
    }

    private bool ShouldDeferDomainHandling() =>
        App.Current?.IsDesktop == true &&
        hasBeenActivated &&
        !IsTabActive;

    private void OnSyncedPreferencesArrived(SessionPreferences prefs)
    {
        if (!UiThreadDispatcher.CheckAccess())
        {
            _ = UiThreadDispatcher.InvokeAsync(() => OnSyncedPreferencesArrived(prefs));
            return;
        }

        if (!viewLoaded) return;

        // Suppress the persist echo: applying inbound sync values must not
        // bounce back through UpdateRecordedAsync.
        recordedPreferencePersistenceEnabled = false;
        try
        {
            ApplyRecordedPreferences(prefs);
        }
        finally
        {
            recordedPreferencePersistenceEnabled = true;
        }
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        await StopLoadedSessionAsync();
    }

    private async Task StopLoadedSessionAsync()
    {
        viewLoaded = false;
        loadOperation.Cancel();
        observedInitialDomain = false;
        promptedRecomputeSignature = null;
        deferredDomainWhileInactive = null;
        latestDomain = null;
        UpdateRecordedSessionExtensionHostState();
        await DisposeRecordedSessionExtensionScopesAsync();
        DisposeScopedSubscriptions();
    }

    #endregion

}
