using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Editor state for a recorded session's detail tab.
/// It owns loaded telemetry presentation, plot and sidebar workspace state,
/// editable notes/settings state, and reactive stale-data prompts for the
/// opened session.
/// </summary>
public sealed partial class SessionDetailViewModel : TabPageViewModelBase,
    IRecordedSessionGraphWorkspace, ISessionMediaWorkspace, ISessionStatisticsWorkspace, ISessionSidebarWorkspace
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
    public SessionTimelineLinkViewModel Timeline { get; } = new();

    #region Private fields

    private readonly SessionCoordinator sessionCoordinator;
    private readonly ISessionStore sessionStore;
    private readonly IRecordedSessionGraph recordedSessionGraph;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly ISessionAnalysisService sessionAnalysisService;
    private readonly ISessionPreferences sessionPreferences;
    private Session session;
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
    private string? promptedRecomputeSignature;
    private bool reportedNotRecomputableStale;
    private bool recordedPreferencePersistenceEnabled; // Prevent property set on creation from re-writing preferences
    private bool viewLoaded;
    private SessionPreferences recordedPreferences = SessionPreferences.Default;
    private SessionPlotPreferences plotPreferences = SessionPreferences.Default.Plots;
    private SurfacePresentationState recordedTravelGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedVelocityGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedImuGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedSpeedGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedElevationGraphBaseState = SurfacePresentationState.Hidden;

    #endregion Private fields

    #region Public fields

    public DamperPageViewModel DamperPage { get; }
    public bool HasMediaContent => MapState.ReservesLayout || VideoState.ReservesLayout;
    public NotesPageViewModel NotesPage { get; } = new();
    public SessionPlotPreferences PlotPreferences
    {
        get => plotPreferences;
        private set => SetProperty(ref plotPreferences, value);
    }
    public PreferencesPageViewModel PreferencesPage { get; } = new();
    public MapViewModel? MapViewModel { get; }

    #endregion Public fields

    #region Observable properties

    [ObservableProperty] private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;
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
    [ObservableProperty] private SurfacePresentationState speedGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState elevationGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SessionGraphLayout graphLayout = SessionGraphLayout.Empty;
    [ObservableProperty] private SurfacePresentationState frontStatisticsState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearStatisticsState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState compressionBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState reboundBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontForkVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontFrameVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearForkVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearFrameVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private TravelHistogramMode selectedTravelHistogramMode = TravelHistogramMode.ActiveSuspension;
    [ObservableProperty] private BalanceDisplacementMode selectedBalanceDisplacementMode = BalanceDisplacementMode.Zenith;
    [ObservableProperty] private VelocityAverageMode selectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;
    [ObservableProperty] private SessionAnalysisTargetProfile selectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMediaContent))]
    private SurfacePresentationState mapState = SurfacePresentationState.Hidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMediaContent))]
    private SurfacePresentationState videoState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SessionDamperPercentages damperPercentages = new(null, null, null, null, null, null, null, null);
    [ObservableProperty] private SessionAnalysisResult sessionAnalysis = SessionAnalysisResult.Hidden;
    public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } =
    [
        new(TravelHistogramMode.ActiveSuspension, "Active suspension", "Uses compression and rebound stroke samples only."),
        new(TravelHistogramMode.DynamicSag, "Dynamic sag", "Uses all selected travel samples."),
    ];
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
    [
        new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at its deepest travel."),
        new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by start-to-end travel distance."),
    ];
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
    [
        new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Uses every stroke sample for bars and average labels."),
        new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Uses one peak-speed event per stroke for bars and average labels."),
    ];
    public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
    [
        new(SessionAnalysisTargetProfile.Weekend, "Weekend", "Uses conservative speed context for recreational pace and mixed terrain."),
        new(SessionAnalysisTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
        new(SessionAnalysisTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
        new(SessionAnalysisTargetProfile.DH, "DH", "Uses downhill-race speed context."),
    ];
    public string SessionAnalysisRangeText => AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";
    public string SessionAnalysisModesText => $"Travel: {DisplayName(SelectedTravelHistogramMode)}  Velocity: {DisplayName(SelectedVelocityAverageMode)}  Balance: {DisplayName(SelectedBalanceDisplacementMode)}";
    public ObservableCollection<PageViewModelBase> Pages { get; }
    IReadOnlyList<TrackPoint>? IRecordedSessionGraphWorkspace.TrackPoints => TrackPoints;

    #endregion Observable properties

    partial void OnTelemetryDataChanged(TelemetryData? value)
    {
        IsComplete = value != null;
        NotesPage.SetTemperatureAverages(value?.TemperatureAverages ?? []);
        pendingAnalysisRangeBoundary = null;
        RefreshTrackTimelineContext();
        if (value is null)
        {
            SessionAnalysis = SessionAnalysisResult.Hidden;
            return;
        }

        if (AnalysisRange is not null)
        {
            ClearAnalysisRange();
            return;
        }

        RecomputeDamperPercentagesForAnalysisRange();
        RecomputeSessionAnalysisIfAllowed();
    }

    partial void OnAnalysisRangeChanged(TelemetryTimeRange? value)
    {
        OnPropertyChanged(nameof(SessionAnalysisRangeText));
        RefreshAnalysisRangeStates();
        RecomputeDamperPercentagesForAnalysisRange();
        RecomputeSessionAnalysisIfAllowed();
    }

    partial void OnSelectedTravelHistogramModeChanged(TravelHistogramMode value)
    {
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnSelectedBalanceDisplacementModeChanged(BalanceDisplacementMode value)
    {
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnSelectedVelocityAverageModeChanged(VelocityAverageMode value)
    {
        OnPropertyChanged(nameof(SessionAnalysisModesText));
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnSelectedSessionAnalysisTargetProfileChanged(SessionAnalysisTargetProfile value)
    {
        RecomputeSessionAnalysis();
        PersistRecordedStatisticsPreferencesIfEnabled();
    }

    partial void OnFullTrackPointsChanged(List<TrackPoint>? value)
    {
        if (MapViewModel is null)
        {
            return;
        }

        MapViewModel.FullTrackPoints = value;
    }

    partial void OnTrackPointsChanged(List<TrackPoint>? value)
    {
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
        if (MapViewModel is null)
        {
            return;
        }

        MapViewModel.TimelineContext = value;
    }

    partial void OnVideoUrlChanged(string? value)
    {
        VideoState = string.IsNullOrWhiteSpace(value)
            ? SurfacePresentationState.Hidden
            : SurfacePresentationState.Ready;
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
        DamperPage.FrontHscPercentage = percentages.FrontHscPercentage;
        DamperPage.RearHscPercentage = percentages.RearHscPercentage;
        DamperPage.FrontLscPercentage = percentages.FrontLscPercentage;
        DamperPage.RearLscPercentage = percentages.RearLscPercentage;
        DamperPage.FrontLsrPercentage = percentages.FrontLsrPercentage;
        DamperPage.RearLsrPercentage = percentages.RearLsrPercentage;
        DamperPage.FrontHsrPercentage = percentages.FrontHsrPercentage;
        DamperPage.RearHsrPercentage = percentages.RearHsrPercentage;
    }

    private void ClearDamperPercentages()
    {
        ApplyDamperPercentages(new SessionDamperPercentages(null, null, null, null, null, null, null, null));
    }

    private void RecomputeDamperPercentagesForAnalysisRange()
    {
        if (TelemetryData is null)
        {
            ClearDamperPercentages();
            return;
        }

        ApplyDamperPercentages(sessionPresentationService.CalculateDamperPercentages(TelemetryData, AnalysisRange));
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
            DamperPercentages,
            SelectedSessionAnalysisTargetProfile));
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string DisplayName(TravelHistogramMode mode)
    {
        return mode == TravelHistogramMode.DynamicSag ? "Dynamic sag" : "Active suspension";
    }

    private static string DisplayName(VelocityAverageMode mode)
    {
        return mode == VelocityAverageMode.StrokePeakAveraged ? "Stroke-peak average" : "Sample-averaged";
    }

    private static string DisplayName(BalanceDisplacementMode mode)
    {
        return mode == BalanceDisplacementMode.Travel ? "Travel" : "Zenith";
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

    private static SurfacePresentationState CreateStatisticsState(
        TelemetryData? telemetry,
        SuspensionType suspensionType,
        TelemetryTimeRange? range)
    {
        if (telemetry is null)
        {
            return SurfacePresentationState.Hidden;
        }

        var suspension = suspensionType == SuspensionType.Front ? telemetry.Front : telemetry.Rear;
        if (!suspension.Present)
        {
            return SurfacePresentationState.Hidden;
        }

        return TelemetryStatistics.HasStrokeData(telemetry, suspensionType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for statistics.");
    }

    private static SurfacePresentationState CreateBalanceState(
        TelemetryData? telemetry,
        BalanceType balanceType,
        TelemetryTimeRange? range)
    {
        if (telemetry is null || !telemetry.Front.Present || !telemetry.Rear.Present)
        {
            return SurfacePresentationState.Hidden;
        }

        return TelemetryStatistics.HasBalanceData(telemetry, balanceType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for balance data.");
    }

    private static SurfacePresentationState CreateVibrationState(
        TelemetryData? telemetry,
        SuspensionType suspensionType,
        ImuLocation location,
        TelemetryTimeRange? range)
    {
        if (telemetry is null)
        {
            return SurfacePresentationState.Hidden;
        }

        var suspension = suspensionType == SuspensionType.Front ? telemetry.Front : telemetry.Rear;
        if (!suspension.Present || !TelemetryStatistics.HasVibrationData(telemetry, location))
        {
            return SurfacePresentationState.Hidden;
        }

        return TelemetryStatistics.HasStrokeData(telemetry, suspensionType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for vibration data.");
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
        FrontStatisticsState = CreateStatisticsState(telemetry, SuspensionType.Front, AnalysisRange);
        RearStatisticsState = CreateStatisticsState(telemetry, SuspensionType.Rear, AnalysisRange);
        CompressionBalanceState = CreateBalanceState(telemetry, BalanceType.Compression, AnalysisRange);
        ReboundBalanceState = CreateBalanceState(telemetry, BalanceType.Rebound, AnalysisRange);
        FrontForkVibrationState = CreateVibrationState(telemetry, SuspensionType.Front, ImuLocation.Fork, AnalysisRange);
        FrontFrameVibrationState = CreateVibrationState(telemetry, SuspensionType.Front, ImuLocation.Frame, AnalysisRange);
        RearForkVibrationState = CreateVibrationState(telemetry, SuspensionType.Rear, ImuLocation.Fork, AnalysisRange);
        RearFrameVibrationState = CreateVibrationState(telemetry, SuspensionType.Rear, ImuLocation.Frame, AnalysisRange);
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
                ApplyDamperPercentages(loaded.Data.DamperPercentages);
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
        if (!frontStatisticsAvailable)
        {
            FrontStatisticsState = SurfacePresentationState.Hidden;
            FrontForkVibrationState = SurfacePresentationState.Hidden;
            FrontFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!rearStatisticsAvailable)
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
        BaselineUpdated = snapshot.Updated;
        IsComplete = snapshot.HasProcessedData;
        lastObservedHasProcessedData = snapshot.HasProcessedData;
        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
    }

    private async Task HandleDomainChangedAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!viewLoaded)
        {
            return;
        }

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

    #endregion

    #region Constructors

    internal SessionDetailViewModel(
        SessionSnapshot snapshot,
        SessionCoordinator sessionCoordinator,
        ISessionStore sessionStore,
        IRecordedSessionGraph recordedSessionGraph,
        ISessionPresentationService sessionPresentationService,
        ISessionAnalysisService sessionAnalysisService,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISessionPreferences sessionPreferences)
        : base(shell, dialogService)
    {
        ArgumentNullException.ThrowIfNull(sessionPreferences);

        this.sessionCoordinator = sessionCoordinator;
        this.sessionStore = sessionStore;
        this.recordedSessionGraph = recordedSessionGraph;
        this.sessionPresentationService = sessionPresentationService;
        this.sessionAnalysisService = sessionAnalysisService;
        this.sessionPreferences = sessionPreferences;
        session = SessionFromSnapshot(snapshot);
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        IsComplete = snapshot.HasProcessedData;
        lastObservedHasProcessedData = snapshot.HasProcessedData;
        GraphPage = new RecordedGraphPageViewModel(this, this);
        SpringPage = new SpringPageViewModel(this);
        StrokesPage = new StrokesPageViewModel(this);
        DamperPage = new DamperPageViewModel(this);
        BalancePage = new BalancePageViewModel(this);
        VibrationPage = new VibrationPageViewModel(this);
        AnalysisPage = new SessionAnalysisPageViewModel(this);
        Pages = [GraphPage, SpringPage, StrokesPage, DamperPage, BalancePage, VibrationPage, AnalysisPage, NotesPage, PreferencesPage];
        MapViewModel = new MapViewModel(tileLayerService, dialogService);
        _ = MapViewModel.InitializeAsync();
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
        PreferencesPage.SpeedPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.ElevationPlot.PropertyChanged += OnPlotPreferenceChanged;

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
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(TrackPoints);
        PreferencesPage.ApplyPlotAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasSpeedSeries,
            hasElevationSeries,
            HasImuTelemetry(telemetry));
    }

    private void ApplyRecordedReadyGraphStates(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(TrackPoints);

        PreferencesPage.ApplyPlotAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasSpeedSeries,
            hasElevationSeries,
            hasImuTelemetry);

        var travelState = hasTravelTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var imuState = hasImuTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var speedState = hasSpeedSeries
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var elevationState = hasElevationSeries
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        SetRecordedGraphBaseStates(travelState, travelState, imuState, speedState, elevationState);
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
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        recordedTravelGraphBaseState = travelState;
        recordedVelocityGraphBaseState = velocityState;
        recordedImuGraphBaseState = imuState;
        recordedSpeedGraphBaseState = speedState;
        recordedElevationGraphBaseState = elevationState;
        RefreshRecordedGraphStates();
    }

    private void RefreshRecordedGraphStates()
    {
        TravelGraphState = recordedTravelGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Travel);
        VelocityGraphState = recordedVelocityGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Velocity);
        ImuGraphState = recordedImuGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Imu);
        SpeedGraphState = recordedSpeedGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Speed);
        ElevationGraphState = recordedElevationGraphBaseState.ApplyPlotSelection(recordedPreferences.Plots.Elevation);
        GraphLayout = SessionGraphLayout.Create(
            TravelGraphState,
            VelocityGraphState,
            ImuGraphState,
            SpeedGraphState,
            ElevationGraphState);
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
        PreferencesPage.ApplyPlotPreferences(preferences.Plots);
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
            SelectedSessionAnalysisTargetProfile);
    }

    private void PersistRecordedStatisticsPreferencesIfEnabled()
    {
        var statistics = CreateStatisticsPreferences();
        recordedPreferences = recordedPreferences with { Statistics = statistics };
        PersistRecordedPreferenceChangeIfEnabled(current => current with { Statistics = statistics });
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

    protected override Task CloseImplementation()
    {
        StopLoadedSession();
        return Task.CompletedTask;
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

    public void SetAnalysisRangeBoundaryFromMarker(double markerSeconds)
    {
        if (TelemetryData is null ||
            double.IsNaN(markerSeconds) ||
            double.IsInfinity(markerSeconds))
        {
            pendingAnalysisRangeBoundary = null;
            return;
        }

        markerSeconds = Math.Clamp(markerSeconds, 0, TelemetryData.Metadata.Duration);
        if (AnalysisRange is { } range)
        {
            if (Math.Abs(markerSeconds - range.StartSeconds) <= Math.Abs(markerSeconds - range.EndSeconds))
            {
                SetAnalysisRange(markerSeconds, range.EndSeconds);
            }
            else
            {
                SetAnalysisRange(range.StartSeconds, markerSeconds);
            }

            return;
        }

        if (pendingAnalysisRangeBoundary is not { } pendingBoundary)
        {
            pendingAnalysisRangeBoundary = markerSeconds;
            return;
        }

        SetAnalysisRange(pendingBoundary, markerSeconds);
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

        await RestoreRecordedPreferencesAsync();
        await RequestLoadAsync();

        if (!viewLoaded)
        {
            return;
        }

        var watch = recordedSessionGraph.WatchSession(Id);
        if (SynchronizationContext.Current is { } synchronizationContext)
        {
            watch = watch.ObserveOn(synchronizationContext);
        }

        EnsureScopedSubscription(s => s.Add(watch.Subscribe(domain => _ = HandleDomainChangedAsync(domain))));
    }

    [RelayCommand]
    private void Unloaded()
    {
        StopLoadedSession();
    }

    private void StopLoadedSession()
    {
        viewLoaded = false;
        loadOperation.Cancel();
        observedInitialDomain = false;
        promptedRecomputeSignature = null;
        DisposeScopedSubscriptions();
    }

    #endregion

}
