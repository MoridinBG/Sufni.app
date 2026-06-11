using Avalonia.Media;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.ExtensionHosting.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services;
using Sufni.App.SessionDetails;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.ViewModels;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Plots;
using Sufni.Telemetry;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Editor state for a recorded session's detail tab.
/// It owns loaded telemetry presentation, plot and sidebar workspace state,
/// editable notes/settings state, and reactive stale-data prompts for the
/// opened session.
/// </summary>
public sealed partial class SessionDetailViewModel : TabPageViewModelBase, IRecordedSessionHostOperations
{
    private enum PresentationMode
    {
        Unknown,
        Desktop,
        Mobile
    }

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
    private readonly RecordedSessionExtensionSlots emptyExtensionSlots = new();
    private readonly RecordedSessionExtensionManager? recordedSessionExtensions;
    private readonly RecordedSessionOperationCoordinator? recordedSessionOperationCoordinator;
    private readonly SessionStalenessReconciler stalenessReconciler;
    private readonly StatisticsSelectionController statisticsSelectionController = new();
    private readonly RecordedPresentationApplier presentationApplier;
    private readonly RecordedPreferenceStore recordedPreferenceStore;
    private readonly Dictionary<string, PageViewModelBase> recordedSessionExtensionPages = [];
    private Session session;
    private RecordedGraphPageViewModel GraphPage { get; }
    private StrokesPageViewModel StrokesPage { get; }
    private SpringPageViewModel SpringPage { get; }
    private BalancePageViewModel BalancePage { get; }
    private VibrationPageViewModel VibrationPage { get; }
    private SessionAnalysisPageViewModel AnalysisPage { get; }

    private readonly CancellableOperation loadOperation = new();
    private SessionPresentationDimensions? lastPresentationDimensions;
    private PresentationMode presentationMode = PresentationMode.Unknown;
    private double? pendingAnalysisRangeBoundary;
    private bool suppressDirtinessEvaluation;
    private bool suppressAnalysisRecompute;
    private bool viewLoaded;
    private bool hasBeenActivated;
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
    private DampingSpeedCutoffOwner? dampingSpeedCutoffOwner;
    private DampingSpeedCutoffs persistedDampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    private DampingSpeedCutoffs? dampingSpeedCutoffPreviewOrigin;

    #endregion Private fields

    #region Public fields

    public DamperPageViewModel DamperPage { get; }
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

            recordedPreferenceStore.UpdateCurrent(current => current with { Graph = value });
            SessionContext.GraphPreferences = value;
            recordedPreferenceStore.PersistChangeIfEnabled(current => current with { Graph = value });
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
    public TelemetryRangeSelection? SelectedFrontRangeSelection => statisticsSelectionController.SelectedFrontRangeSelection;
    public TelemetryRangeSelection? SelectedRearRangeSelection => statisticsSelectionController.SelectedRearRangeSelection;
    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> PlotContextMenuActionsByRowId { get; }
    public bool CanEditDampingSpeedCutoffs => dampingSpeedCutoffOwner is not null;
    public RecordedSessionExtensionSlots ExtensionSlots => recordedSessionExtensions?.ExtensionSlots ?? emptyExtensionSlots;

    #endregion Public fields

    #region Observable properties

    [ObservableProperty] private TelemetryData? telemetryData;
    [ObservableProperty] private TelemetryTimeRange? analysisRange;
    [ObservableProperty] private bool isComplete;
    [ObservableProperty] private TravelHistogramMode selectedTravelHistogramMode = TravelHistogramMode.ActiveSuspension;
    [ObservableProperty] private BalanceDisplacementMode selectedBalanceDisplacementMode = BalanceDisplacementMode.Zenith;
    [ObservableProperty] private BalanceSpeedMode selectedBalanceSpeedMode = BalanceSpeedMode.Both;
    [ObservableProperty] private VelocityAverageMode selectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;
    [ObservableProperty] private SessionAnalysisTargetProfile selectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail;
    [ObservableProperty] private DampingSpeedCutoffs dampingSpeedCutoffs = DampingSpeedCutoffs.Default;
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
            SessionContext.SessionAnalysis = SessionAnalysisResult.Hidden;
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
        presentationApplier.RefreshAnalysisRangeStates();
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

    #region Private methods

    private static SessionPresentationDimensions? CreatePresentationDimensions(Rect? bounds)
    {
        if (bounds is not Rect rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        return new SessionPresentationDimensions((int)rect.Width, (int)(rect.Height / 2.0));
    }

    internal void ApplyDamperPercentages(SessionDamperPercentages percentages)
    {
        SessionContext.DamperPercentages = percentages;
        DamperPage.ApplyDamperPercentages(percentages);
        UpdateRecordedSessionExtensionHostState();
    }

    internal void ApplyDampingSpeedCutoffContext(
        DampingSpeedCutoffs cutoffs,
        DampingSpeedCutoffOwner? owner)
    {
        persistedDampingSpeedCutoffs = cutoffs.ClampValues();
        dampingSpeedCutoffPreviewOrigin = null;
        dampingSpeedCutoffOwner = owner;
        SessionContext.CanEditDampingSpeedCutoffs = dampingSpeedCutoffOwner is not null;
        OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
        SessionContext.PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
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
            DampingCutoffInteraction.RoundDragValue(cutoffMmPerSecond));
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
            DampingCutoffInteraction.RoundDragValue(cutoffMmPerSecond));
        DampingSpeedCutoffs = committedCutoffs;
        SessionContext.PlotDampingSpeedCutoffs = committedCutoffs;

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
                SessionContext.PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
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

    internal void ApplyModeAwareDamperPercentages(SessionDamperPercentages sampleAveragedPercentages)
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

    internal void RecomputeSessionAnalysis()
    {
        SessionContext.SessionAnalysis = sessionAnalysisService.Analyze(new SessionAnalysisRequest(
            TelemetryData,
            AnalysisRange,
            SelectedTravelHistogramMode,
            SelectedVelocityAverageMode,
            SelectedBalanceDisplacementMode,
            SelectedBalanceSpeedMode,
            SessionContext.DamperPercentages,
            SelectedSessionAnalysisTargetProfile)
        {
            DampingSpeedCutoffs = this.DampingSpeedCutoffs,
        });
    }

    internal Guid? CurrentSessionFullTrack => session.FullTrack;

    internal SessionPlotPreferences RecordedPlotPreferences => recordedPreferenceStore.Current.Plots;

    internal void SetSessionFullTrack(Guid? fullTrackId)
    {
        session.FullTrack = fullTrackId;
    }

    internal void ApplyTelemetryDataWithoutAnalysisRecompute(TelemetryData? value)
    {
        suppressAnalysisRecompute = true;
        try
        {
            TelemetryData = value;
        }
        finally
        {
            suppressAnalysisRecompute = false;
        }
    }

    private void RefreshTrackTimelineContext()
    {
        SessionContext.TrackTimelineContext = TelemetryData is { } telemetry
            ? TrackPointSeries.BuildTimelineContext(
                SessionContext.TrackPoints,
                telemetry.Metadata.Timestamp,
                telemetry.Metadata.Duration)
            : null;
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
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
            presentationApplier.ClearRecordedPresentation();
            presentationApplier.ApplyRecordedLoadingStates(currentSnapshot.FullTrackId is not null);
        }
        else
        {
            SessionContext.ScreenState = SessionScreenPresentationState.Ready;
        }

        try
        {
            if (presentationMode == PresentationMode.Desktop)
            {
                var result = await sessionCoordinator.LoadDesktopDetailAsync(Id, token);
                if (token.IsCancellationRequested) return;
                presentationApplier.ApplyDesktopLoadResult(result);
                return;
            }

            if (presentationMode == PresentationMode.Unknown)
            {
                return;
            }

            if (lastPresentationDimensions is null) return;

            var mobileResult = await sessionCoordinator.LoadMobileDetailAsync(
                Id, lastPresentationDimensions.Value, token);
            if (token.IsCancellationRequested) return;
            presentationApplier.ApplyMobileLoadResult(mobileResult);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task ApplyPersistedSnapshotAsync(SessionSnapshot snapshot)
    {
        session = SessionFromSnapshot(snapshot);
        SessionContext.SessionSnapshot = snapshot;
        BaselineUpdated = snapshot.Updated;
        IsComplete = snapshot.HasProcessedData;
        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
        UpdateRecordedSessionExtensionHostState();
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
                SessionContext.TrackTimelineContext,
                timelineDurationSeconds,
                Timeline),
            new RecordedSessionStatisticsState(
                SessionContext.DamperPercentages,
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
        SessionContext.SessionOperationState = SessionOperationPresentationState.Progress(message, percent);
    }

    private void CompleteRecordedSessionExtensionOperation()
    {
        SessionContext.SessionOperationState = SessionOperationPresentationState.Hidden;
    }

    private void SetRecordedSessionExtensionTimelineVisibleRange(
        double startNormalized,
        double endNormalized,
        object source)
    {
        Timeline.SetVisibleRange(startNormalized, endNormalized, source);
    }

    void IRecordedSessionHostOperations.SetTimelineVisibleRange(
        double startNormalized,
        double endNormalized,
        object source) =>
        SetRecordedSessionExtensionTimelineVisibleRange(startNormalized, endNormalized, source);

    void IRecordedSessionHostOperations.AddError(string message) => ErrorMessages.Add(message);

    void IRecordedSessionHostOperations.AddNotification(string message) => Notifications.Add(message);

    IRecordedSessionOperationLease IRecordedSessionHostOperations.StartOperation(string description) =>
        (recordedSessionOperationCoordinator
            ?? throw new InvalidOperationException("Recorded-session extension hosting is not configured."))
        .StartOperation(description);

    void IRecordedSessionHostOperations.RequestPageSelection(string contributionId) =>
        RequestRecordedSessionExtensionPageSelection(contributionId);

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
        recordedPreferenceStore = new RecordedPreferenceStore(
            sessionPreferences,
            () => Id,
            ErrorMessages.Add);
        showAirtimeAction = CreateAirtimeAction("travel_airtime", SessionContext.ShowAirtime, () => SessionContext.ShowAirtime = !SessionContext.ShowAirtime);
        showVelocityAirtimeAction = CreateAirtimeAction("velocity_airtime", SessionContext.ShowVelocityAirtime, () => SessionContext.ShowVelocityAirtime = !SessionContext.ShowVelocityAirtime);
        showImuAirtimeAction = CreateAirtimeAction("imu_airtime", SessionContext.ShowImuAirtime, () => SessionContext.ShowImuAirtime = !SessionContext.ShowImuAirtime);
        showPitchRollAirtimeAction = CreateAirtimeAction("pitch_roll_airtime", SessionContext.ShowPitchRollAirtime, () => SessionContext.ShowPitchRollAirtime = !SessionContext.ShowPitchRollAirtime);
        showSpeedAirtimeAction = CreateAirtimeAction("speed_airtime", SessionContext.ShowSpeedAirtime, () => SessionContext.ShowSpeedAirtime = !SessionContext.ShowSpeedAirtime);
        showElevationAirtimeAction = CreateAirtimeAction("elevation_airtime", SessionContext.ShowElevationAirtime, () => SessionContext.ShowElevationAirtime = !SessionContext.ShowElevationAirtime);
        showStatisticsSelectionAction = CreateStatisticsSelectionAction("travel_statistics_selection", SessionContext.ShowStatisticsSelection, () => SessionContext.ShowStatisticsSelection = !SessionContext.ShowStatisticsSelection);
        showVelocityStatisticsSelectionAction = CreateStatisticsSelectionAction("velocity_statistics_selection", SessionContext.ShowVelocityStatisticsSelection, () => SessionContext.ShowVelocityStatisticsSelection = !SessionContext.ShowVelocityStatisticsSelection);
        showImuStatisticsSelectionAction = CreateStatisticsSelectionAction("imu_statistics_selection", SessionContext.ShowImuStatisticsSelection, () => SessionContext.ShowImuStatisticsSelection = !SessionContext.ShowImuStatisticsSelection);
        showPitchRollStatisticsSelectionAction = CreateStatisticsSelectionAction("pitch_roll_statistics_selection", SessionContext.ShowPitchRollStatisticsSelection, () => SessionContext.ShowPitchRollStatisticsSelection = !SessionContext.ShowPitchRollStatisticsSelection);
        showSpeedStatisticsSelectionAction = CreateStatisticsSelectionAction("speed_statistics_selection", SessionContext.ShowSpeedStatisticsSelection, () => SessionContext.ShowSpeedStatisticsSelection = !SessionContext.ShowSpeedStatisticsSelection);
        showElevationStatisticsSelectionAction = CreateStatisticsSelectionAction("elevation_statistics_selection", SessionContext.ShowElevationStatisticsSelection, () => SessionContext.ShowElevationStatisticsSelection = !SessionContext.ShowElevationStatisticsSelection);
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
        stalenessReconciler = new SessionStalenessReconciler(
            sessionCoordinator,
            sessionStore,
            dialogService,
            () => Id,
            () => BaselineUpdated,
            value => BaselineUpdated = value,
            () => IsDirty,
            () => viewLoaded,
            ShouldDeferDomainHandling,
            ApplyPersistedSnapshotAsync,
            RequestLoadAsync,
            UpdateRecordedSessionExtensionHostState,
            ErrorMessages.Add);
        if (extensionDatabase is not null && recordedSessionDataReader is not null && backgroundTaskRunner is not null)
        {
            recordedSessionOperationCoordinator = new RecordedSessionOperationCoordinator(
                ReportRecordedSessionExtensionOperation,
                CompleteRecordedSessionExtensionOperation);
            recordedSessionExtensions = new RecordedSessionExtensionManager(
                Id,
                extensionFactories,
                extensionDatabase,
                recordedSessionDataReader,
                backgroundTaskRunner,
                uiThreadDispatcher,
                recordedSessionOperationCoordinator,
                this);
            recordedSessionExtensions.ExtensionSlots.Pages.CollectionChanged += OnRecordedSessionExtensionPagesChanged;
        }
        SessionContext.ExtensionSlots = ExtensionSlots;
        SessionContext.PropertyChanged += OnSessionContextPropertyChanged;

        GraphPage = new RecordedGraphPageViewModel(GraphWorkspace, MediaWorkspace);
        SpringPage = new SpringPageViewModel(StatisticsWorkspace);
        StrokesPage = new StrokesPageViewModel(StatisticsWorkspace);
        DamperPage = new DamperPageViewModel(StatisticsWorkspace);
        BalancePage = new BalancePageViewModel(StatisticsWorkspace);
        VibrationPage = new VibrationPageViewModel(StatisticsWorkspace);
        AnalysisPage = new SessionAnalysisPageViewModel(StatisticsWorkspace);
        presentationApplier = new RecordedPresentationApplier(
            this,
            SessionContext,
            Pages,
            SpringPage,
            DamperPage,
            BalancePage,
            VibrationPage,
            AnalysisPage,
            NotesPage,
            PreferencesPage);
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
            presentationApplier.ApplyRecordedLoadingStates(snapshot.FullTrackId is not null);
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
            IconPathData =
                "M12 4C7 4 3 7 1 12C3 17 7 20 12 20C17 20 21 17 23 12C21 7 17 4 12 4ZM12 16C9.8 16 8 14.2 8 12C8 9.8 9.8 8 12 8C14.2 8 16 9.8 16 12C16 14.2 14.2 16 12 16Z",
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
            IconPathData = "M4 6H20V8H4V6ZM4 11H17V13H4V11ZM4 16H13V18H4V16Z",
            Command = new RelayCommand(toggle),
            Tone = TelemetryPlotRowActionTone.Default,
        };
        UpdateStatisticsSelectionAction(action, isChecked, SessionContext.HasStatisticsSelection);
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
        statisticsSelectionController.Clear();
        SyncStatisticsSelectionController();
        ClearStatisticsSelectionToggles();
        RefreshStatisticsSelectionActionStates();
    }

    private void ClearDampingRangeSelections()
    {
        if (!statisticsSelectionController.ClearDampingRangeSelections(TelemetryData, AnalysisRange))
        {
            return;
        }

        SyncStatisticsSelectionController();
        if (!SessionContext.HasStatisticsSelection)
        {
            ClearStatisticsSelectionToggles();
        }

        RefreshStatisticsSelectionActionStates();
    }

    private void SyncStatisticsSelectionController()
    {
        SessionContext.SelectedFrontRangeSelection = statisticsSelectionController.SelectedFrontRangeSelection;
        SessionContext.SelectedRearRangeSelection = statisticsSelectionController.SelectedRearRangeSelection;
        OnPropertyChanged(nameof(SelectedFrontRangeSelection));
        OnPropertyChanged(nameof(SelectedRearRangeSelection));
        SessionContext.StatisticsSelectionHighlightRanges = statisticsSelectionController.HighlightRanges;
    }

    private void RefreshStatisticsSelectionActionStates()
    {
        var hasSelection = SessionContext.HasStatisticsSelection;
        UpdateStatisticsSelectionAction(showStatisticsSelectionAction, SessionContext.ShowStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showVelocityStatisticsSelectionAction, SessionContext.ShowVelocityStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showImuStatisticsSelectionAction, SessionContext.ShowImuStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showPitchRollStatisticsSelectionAction, SessionContext.ShowPitchRollStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showSpeedStatisticsSelectionAction, SessionContext.ShowSpeedStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showElevationStatisticsSelectionAction, SessionContext.ShowElevationStatisticsSelection, hasSelection);
    }


    private void OnSessionContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(RecordedSessionContext.FullTrackPoints):
                if (MapViewModel is not null)
                {
                    MapViewModel.FullTrackPoints = SessionContext.FullTrackPoints;
                }

                break;
            case nameof(RecordedSessionContext.TrackPoints):
                if (MapViewModel is not null)
                {
                    MapViewModel.SessionTrackPoints = SessionContext.TrackPoints;
                }

                RefreshTrackTimelineContext();
                if (TelemetryData is not null)
                {
                    presentationApplier.ApplyRecordedTrackGraphStates();
                }

                break;
            case nameof(RecordedSessionContext.TrackTimelineContext):
                if (MapViewModel is not null)
                {
                    MapViewModel.TimelineContext = SessionContext.TrackTimelineContext;
                }

                UpdateRecordedSessionExtensionHostState();
                break;
            case nameof(RecordedSessionContext.ShowAirtime):
                UpdateAirtimeAction(showAirtimeAction, SessionContext.ShowAirtime);
                break;
            case nameof(RecordedSessionContext.ShowVelocityAirtime):
                UpdateAirtimeAction(showVelocityAirtimeAction, SessionContext.ShowVelocityAirtime);
                break;
            case nameof(RecordedSessionContext.ShowImuAirtime):
                UpdateAirtimeAction(showImuAirtimeAction, SessionContext.ShowImuAirtime);
                break;
            case nameof(RecordedSessionContext.ShowPitchRollAirtime):
                UpdateAirtimeAction(showPitchRollAirtimeAction, SessionContext.ShowPitchRollAirtime);
                break;
            case nameof(RecordedSessionContext.ShowSpeedAirtime):
                UpdateAirtimeAction(showSpeedAirtimeAction, SessionContext.ShowSpeedAirtime);
                break;
            case nameof(RecordedSessionContext.ShowElevationAirtime):
                UpdateAirtimeAction(showElevationAirtimeAction, SessionContext.ShowElevationAirtime);
                break;
            case nameof(RecordedSessionContext.ShowStatisticsSelection):
                UpdateStatisticsSelectionAction(showStatisticsSelectionAction, SessionContext.ShowStatisticsSelection, SessionContext.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowVelocityStatisticsSelection):
                UpdateStatisticsSelectionAction(showVelocityStatisticsSelectionAction, SessionContext.ShowVelocityStatisticsSelection, SessionContext.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowImuStatisticsSelection):
                UpdateStatisticsSelectionAction(showImuStatisticsSelectionAction, SessionContext.ShowImuStatisticsSelection, SessionContext.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowPitchRollStatisticsSelection):
                UpdateStatisticsSelectionAction(showPitchRollStatisticsSelectionAction, SessionContext.ShowPitchRollStatisticsSelection, SessionContext.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowSpeedStatisticsSelection):
                UpdateStatisticsSelectionAction(showSpeedStatisticsSelectionAction, SessionContext.ShowSpeedStatisticsSelection, SessionContext.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowElevationStatisticsSelection):
                UpdateStatisticsSelectionAction(showElevationStatisticsSelectionAction, SessionContext.ShowElevationStatisticsSelection, SessionContext.HasStatisticsSelection);
                break;
        }
    }

    private void ClearStatisticsSelectionToggles()
    {
        SessionContext.ShowStatisticsSelection = false;
        SessionContext.ShowVelocityStatisticsSelection = false;
        SessionContext.ShowImuStatisticsSelection = false;
        SessionContext.ShowPitchRollStatisticsSelection = false;
        SessionContext.ShowSpeedStatisticsSelection = false;
        SessionContext.ShowElevationStatisticsSelection = false;
    }

    private void EvaluateDirtinessFromPageChange()
    {
        if (suppressDirtinessEvaluation)
        {
            return;
        }

        EvaluateDirtiness();
    }

    private async Task RestoreRecordedPreferencesAsync()
    {
        await recordedPreferenceStore.RestoreAsync(ApplyRecordedPreferences);
    }

    private void ApplyRecordedPreferences(SessionPreferences preferences)
    {
        PlotPreferences = preferences.Plots;
        GraphPreferences = preferences.Graph;
        PreferencesPage.ApplyPlotPreferences(preferences.Plots);
        PreferencesPage.ApplyProcessingPreferences(preferences.Processing);
        ApplyRecordedStatisticsPreferences(preferences.Statistics);
        presentationApplier.RefreshRecordedGraphStates(recordedPreferenceStore.Current.Plots);
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
        recordedPreferenceStore.UpdateCurrent(current => current with { Plots = plots });
        presentationApplier.RefreshRecordedGraphStates(recordedPreferenceStore.Current.Plots);
        recordedPreferenceStore.PersistChangeIfEnabled(current => current with { Plots = plots });
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
        recordedPreferenceStore.UpdateCurrent(current => current with { Statistics = statistics });
        recordedPreferenceStore.PersistChangeIfEnabled(current => current with { Statistics = statistics });
    }

    private void OnProcessingPreferenceChangeCommitted(object? sender, EventArgs args)
    {
        _ = PersistRecordedProcessingPreferenceAndRecomputeAsync();
    }

    private async Task PersistRecordedProcessingPreferenceAndRecomputeAsync()
    {
        if (!recordedPreferenceStore.PersistenceEnabled || !viewLoaded)
        {
            return;
        }

        if (!recordedPreferenceStore.TryBeginProcessingPreferenceRecompute())
        {
            PreferencesPage.ApplyProcessingPreferences(recordedPreferenceStore.Current.Processing);
            return;
        }

        var processing = PreferencesPage.CreateProcessingPreferences();
        if (processing == recordedPreferenceStore.Current.Processing)
        {
            recordedPreferenceStore.EndProcessingPreferenceRecompute();
            return;
        }

        try
        {
            if (IsDirty)
            {
                var confirmed = await dialogService.ShowConfirmationAsync(
                    "Recompute session?",
                    "Changing the velocity filter recomputes this session and will discard unsaved changes.");
                if (!confirmed)
                {
                    PreferencesPage.ApplyProcessingPreferences(recordedPreferenceStore.Current.Processing);
                    return;
                }

                if (sessionStore.Get(Id) is { } current)
                {
                    await ApplyPersistedSnapshotAsync(current);
                }
            }

            var previousProcessing = recordedPreferenceStore.Current.Processing;
            recordedPreferenceStore.UpdateCurrent(current => current with { Processing = processing });
            if (!await recordedPreferenceStore.PersistChangeAsync(current => current with { Processing = processing }))
            {
                recordedPreferenceStore.UpdateCurrent(current => current with { Processing = previousProcessing });
                PreferencesPage.ApplyProcessingPreferences(recordedPreferenceStore.Current.Processing);
                return;
            }

            var result = await sessionCoordinator.RecomputeAsync(Id, BaselineUpdated);
            await stalenessReconciler.ApplyRecomputeResultAsync(result);
        }
        finally
        {
            recordedPreferenceStore.EndProcessingPreferenceRecompute();
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
        if (!statisticsSelectionController.Select(selection, TelemetryData, AnalysisRange)) return;

        SyncStatisticsSelectionController();
        ClearStatisticsSelectionToggles();
        if (SessionContext.HasStatisticsSelection)
        {
            SessionContext.ShowStatisticsSelection = true;
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
        presentationMode = bounds is null ? PresentationMode.Desktop : PresentationMode.Mobile;
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
            s.Add(recordedPreferenceStore.Observe().Subscribe(OnSyncedPreferencesArrived));
            s.Add(watch.Subscribe(domain => _ = stalenessReconciler.HandleDomainChangedAsync(domain)));
        });

        await InitializeRecordedSessionExtensionsAsync();
        await RestoreRecordedPreferencesAsync();
        await RequestLoadAsync();
    }

    protected override void OnActivated()
    {
        hasBeenActivated = true;
        UpdateRecordedSessionExtensionHostState();

        if (!viewLoaded)
        {
            return;
        }

        _ = stalenessReconciler.HandleDeferredDomainAsync();
    }

    protected override void OnDeactivated()
    {
        UpdateRecordedSessionExtensionHostState();
    }

    private bool ShouldDeferDomainHandling() =>
        presentationMode == PresentationMode.Desktop &&
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

        recordedPreferenceStore.ApplyWithoutPersisting(prefs, ApplyRecordedPreferences);
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
        stalenessReconciler.ResetForUnload();
        UpdateRecordedSessionExtensionHostState();
        await DisposeRecordedSessionExtensionScopesAsync();
        DisposeScopedSubscriptions();
    }

    #endregion

}
