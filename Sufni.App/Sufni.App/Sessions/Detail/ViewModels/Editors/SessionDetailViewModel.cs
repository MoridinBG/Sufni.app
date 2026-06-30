using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
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

using Sufni.App.Acquisition.Models;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Extensibility.Views;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Analysis.ViewModels.SessionPages;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
using Sufni.App.Sessions.Graph.ViewModels.SessionPages;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Statistics.ViewModels.Editors;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Media.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using Sufni.App.Sessions.Presentation;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>
/// Editor state for a recorded session's detail tab.
/// It owns loaded telemetry presentation, plot and sidebar workspace state,
/// editable notes/settings state, and reactive stale-data prompts for the
/// opened session.
/// </summary>
public sealed partial class SessionDetailViewModel : TabPageViewModelBase, ISessionOperationGateway
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
    private readonly ITrackCoordinator trackCoordinator;
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly ISessionStore sessionStore;
    private readonly IRecordedSessionGraph recordedSessionGraph;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly ISessionAnalysisService sessionAnalysisService;
    private readonly RecordedSessionExtensionSlots emptyExtensionSlots = new();
    private readonly RecordedSessionExtensionManager? recordedSessionExtensions;
    private readonly RecordedSessionOperationCoordinator? recordedSessionOperationCoordinator;
    private readonly SessionStalenessReconciler stalenessReconciler;
    private readonly IRecordedSessionProcessingOptionCache recordedSessionProcessingOptionCache;
    private bool observedInitialDomain;
    private RecordedSessionDomainSnapshot? deferredDomain;
    private readonly StatisticsSelectionController statisticsSelectionController = new();
    private readonly RecordedPresentationApplier presentationApplier;
    private readonly RecordedPreferenceStore recordedPreferenceStore;
    private readonly RecordedSessionExtensionPagesController? extensionPagesController;
    private readonly ProcessingPreferenceWorkflow processingPreferenceWorkflow;
    private Session session;
    private RecordedGraphPageViewModel GraphPage { get; }
    private StrokesPageViewModel StrokesPage { get; }
    private SpringPageViewModel SpringPage { get; }
    private BalancePageViewModel BalancePage { get; }
    private VibrationPageViewModel VibrationPage { get; }
    private SessionAnalysisPageViewModel AnalysisPage { get; }

    private readonly CancellableOperation loadOperation = new();
    private SessionPresentationDimensions? lastPresentationDimensions;
    private double? pendingAnalysisRangeBoundary;
    private RecordedSessionTimelineAlignmentMark? pendingTimelineAlignmentMark;
    private bool suppressDirtinessEvaluation;
    private bool suppressAnalysisRecompute;
    // Set when the user declines to reload after an external metadata edit landed
    // on a dirty draft: BaselineUpdated is pinned below that edit so the next save
    // still conflicts. A later derived-only emission must not advance the baseline
    // past the unacknowledged edit, or the conflict would be silently lost.
    private bool metadataConflictPending;
    private bool viewLoaded;
    private bool hasBeenActivated;
    private SessionPlotPreferences plotPreferences = SessionPreferences.Default.Plots;
    private SessionGraphPreferences graphPreferences = SessionPreferences.Default.Graph;
    private SessionLayoutPreferences layoutPreferences = SessionPreferences.Default.Layout;
    private readonly SessionPlotRowActionsController plotRowActions;
    private readonly PlotAutozoomController plotAutozoomController;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> markGpsEventCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markGpsTelemetryEventCommand;
    private readonly DamperCutoffWorkflow damperCutoffWorkflow;
    private readonly ISessionLayoutStrategy layoutStrategy;

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

    public SessionLayoutPreferences LayoutPreferences
    {
        get => layoutPreferences;
        set
        {
            if (!SetProperty(ref layoutPreferences, value))
            {
                return;
            }

            recordedPreferenceStore.UpdateCurrent(current => current with { Layout = value });
            SessionContext.LayoutPreferences = value;
            OnPropertyChanged(nameof(MediaLayoutPreferences));
            recordedPreferenceStore.PersistChangeIfEnabled(current => current with { Layout = value });
        }
    }

    public SessionPaneGroupPreferences? MediaLayoutPreferences
    {
        get => LayoutPreferences.DesktopMediaRows;
        set => LayoutPreferences = LayoutPreferences with { DesktopMediaRows = value };
    }

    public TelemetrySourceVisibilityStore SourceVisibility => SessionContext.SourceVisibility;
    public PreferencesPageViewModel PreferencesPage { get; } = new();
    public MapViewModel? MapViewModel => SessionContext.MapViewModel;
    public IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions => plotRowActions.TravelHeaderActions;
    public IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions => plotRowActions.VelocityHeaderActions;
    public IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions => plotRowActions.ImuHeaderActions;
    public IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions => plotRowActions.PitchRollHeaderActions;
    public IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions => plotRowActions.SpeedHeaderActions;
    public IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions => plotRowActions.ElevationHeaderActions;
    public TelemetryRangeSelection? SelectedFrontRangeSelection => statisticsSelectionController.SelectedFrontRangeSelection;
    public TelemetryRangeSelection? SelectedRearRangeSelection => statisticsSelectionController.SelectedRearRangeSelection;
    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> PlotContextMenuActionsByRowId { get; }
    public bool CanEditDampingSpeedCutoffs => damperCutoffWorkflow.CanEdit;
    public RecordedSessionExtensionSlots ExtensionSlots => recordedSessionExtensions?.ExtensionSlots ?? emptyExtensionSlots;

    #endregion Public fields

    #region Observable properties

    [ObservableProperty] private bool isComplete;
    public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } = SessionAnalysisPresentation.TravelHistogramModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionAnalysisPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionAnalysisPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionAnalysisPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } = SessionAnalysisPresentation.SessionAnalysisTargetProfileOptions;
    public string SessionAnalysisRangeText => SessionContext.AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";
    public string SessionAnalysisModesText => SessionAnalysisPresentation.DescribeModes(
        SessionContext.SelectedTravelHistogramMode,
        SessionContext.SelectedVelocityAverageMode,
        SessionContext.SelectedBalanceDisplacementMode,
        SessionContext.SelectedBalanceSpeedMode);
    public ObservableCollection<PageViewModelBase> Pages => SessionContext.Pages;

    #endregion Observable properties

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
        damperCutoffWorkflow.ApplyContext(cutoffs, owner);
        OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
    }

    public void PreviewDampingSpeedCutoff(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond) =>
        damperCutoffWorkflow.Preview(side, circuit, cutoffMmPerSecond);

    public void CancelDampingSpeedCutoffPreview() => damperCutoffWorkflow.CancelPreview();

    public Task CommitDampingSpeedCutoffAsync(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond) =>
        damperCutoffWorkflow.CommitAsync(side, circuit, cutoffMmPerSecond);

    private void ClearDamperPercentages()
    {
        ApplyDamperPercentages(SessionDamperPercentages.Empty);
    }

    private void RecomputeDamperPercentagesForAnalysisRange()
    {
        if (SessionContext.TelemetryData is null)
        {
            ClearDamperPercentages();
            return;
        }

        ApplyDamperPercentages(sessionPresentationService.CalculateDamperPercentages(
            SessionContext.TelemetryData,
            SessionContext.AnalysisRange,
            SessionContext.SelectedVelocityAverageMode,
            SessionContext.DampingSpeedCutoffs));
    }

    internal void ApplyModeAwareDamperPercentages(SessionDamperPercentages sampleAveragedPercentages)
    {
        if (SessionContext.TelemetryData is null)
        {
            ClearDamperPercentages();
            return;
        }

        if (SessionContext.AnalysisRange is null && SessionContext.SelectedVelocityAverageMode == VelocityAverageMode.SampleAveraged)
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
            SessionContext.TelemetryData,
            SessionContext.AnalysisRange,
            SessionContext.SelectedTravelHistogramMode,
            SessionContext.SelectedVelocityAverageMode,
            SessionContext.SelectedBalanceDisplacementMode,
            SessionContext.SelectedBalanceSpeedMode,
            SessionContext.DamperPercentages,
            SessionContext.SelectedSessionAnalysisTargetProfile)
        {
            DampingSpeedCutoffs = SessionContext.DampingSpeedCutoffs,
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
            SessionContext.TelemetryData = value;
        }
        finally
        {
            suppressAnalysisRecompute = false;
        }
    }

    private void RefreshTrackTimelineContext()
    {
        SessionContext.TrackTimelineContext = SessionContext.TelemetryData is { } telemetry
            ? TrackPointSeries.BuildTimelineContext(
                SessionContext.TrackPoints,
                telemetry.Metadata.Timestamp + NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
                telemetry.Metadata.Duration)
            : null;
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreatePlotContextMenuActionsByRowId(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        IRelayCommand<TelemetryPlotContextMenuContext?> markGpsEventCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markGpsTelemetryEventCommand)
    {
        var markGpsEvent = new TelemetryPlotContextMenuAction(
            "gps-mark-gps-event",
            "Mark GPS event here",
            markGpsEventCommand);
        var markGpsTelemetryEvent = new TelemetryPlotContextMenuAction(
            "gps-mark-telemetry-event",
            "Mark telemetry event here",
            markGpsTelemetryEventCommand);

        return CreatePlotContextMenuActionsByRowId(
            baseActions,
            markGpsEvent,
            markGpsTelemetryEvent);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreatePlotContextMenuActionsByRowId(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        TelemetryPlotContextMenuAction markGpsEvent,
        TelemetryPlotContextMenuAction markGpsTelemetryEvent)
    {
        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
        {
            [TelemetryGraphRowIds.Travel] = AppendContextMenuActions(baseActions, TelemetryGraphRowIds.Travel, markGpsEvent, markGpsTelemetryEvent),
            [TelemetryGraphRowIds.Velocity] = AppendContextMenuActions(baseActions, TelemetryGraphRowIds.Velocity, markGpsEvent, markGpsTelemetryEvent),
            [TelemetryGraphRowIds.Imu] = AppendContextMenuActions(baseActions, TelemetryGraphRowIds.Imu, markGpsEvent, markGpsTelemetryEvent),
            [TelemetryGraphRowIds.PitchRoll] = AppendContextMenuActions(baseActions, TelemetryGraphRowIds.PitchRoll, markGpsEvent, markGpsTelemetryEvent),
            [TelemetryGraphRowIds.Speed] = AppendContextMenuActions(baseActions, TelemetryGraphRowIds.Speed, markGpsEvent, markGpsTelemetryEvent),
            [TelemetryGraphRowIds.Elevation] = AppendContextMenuActions(baseActions, TelemetryGraphRowIds.Elevation, markGpsEvent, markGpsTelemetryEvent),
        };
    }

    private static IReadOnlyList<TelemetryPlotContextMenuAction> AppendContextMenuActions(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        string rowId,
        params TelemetryPlotContextMenuAction[] actions)
    {
        return baseActions.TryGetValue(rowId, out var rowActions)
            ? [.. rowActions, .. actions]
            : actions;
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
            await layoutStrategy.LoadDetailAsync(
                sessionCoordinator,
                Id,
                lastPresentationDimensions,
                presentationApplier,
                token);
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
        metadataConflictPending = false;
        IsComplete = snapshot.HasProcessedData;
        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
        UpdateRecordedSessionExtensionHostState();
    }

    // Single entry point for graph emissions on the opened session. It treats the
    // derived and metadata axes orthogonally: derived telemetry always refreshes
    // (even while a metadata prompt is pending), and the metadata prompt is decided
    // independently so unsaved edits are never silently discarded.
    private async Task OnDomainChangedAsync(RecordedSessionDomainSnapshot domain)
    {
        // The watch subscription is fire-and-forget; an unguarded throw would
        // surface only as an unobserved task exception.
        try
        {
            await OnDomainChangedCoreAsync(domain);
        }
        catch (Exception exception)
        {
            ErrorMessages.Add($"Failed to handle a session change: {exception.Message}");
        }
    }

    private async Task OnDomainChangedCoreAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!viewLoaded)
        {
            return;
        }

        UpdateRecordedSessionExtensionHostState();

        if (ShouldDeferDomainHandling())
        {
            deferredDomain = domain;
            return;
        }

        deferredDomain = null;

        var initial = !observedInitialDomain;
        observedInitialDomain = true;

        if (!initial)
        {
            // The initial replay already matches the loaded state, so only real
            // changes drive the derived/metadata reaction.
            await ApplyDomainReactionAsync(domain);
        }

        await stalenessReconciler.HandleStalenessAsync(
            domain,
            initial ? RecomputeReason.StaleOnOpen : RecomputeReason.DependencyChanged);
    }

    private async Task ApplyDomainReactionAsync(RecordedSessionDomainSnapshot domain)
    {
        var snapshot = domain.Session;
        var metadataChanged = domain.ChangeKind.HasFlag(DerivedChangeKind.SessionMetadataChanged);
        var reloadTelemetry =
            domain.ChangeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged) ||
            domain.ChangeKind.HasFlag(DerivedChangeKind.FingerprintChanged) ||
            domain.ChangeKind.HasFlag(DerivedChangeKind.DerivedTrackChanged);

        if (metadataChanged && IsDirty)
        {
            // Refresh derived telemetry even while the discard prompt is pending so
            // plots never show stale data; unsaved edits are preserved.
            RefreshDerivedState(snapshot);
            if (reloadTelemetry)
            {
                await RequestLoadAsync();
            }

            var discard = await dialogService.ShowConfirmationAsync(
                "Session changed elsewhere",
                "This session has been updated from another source. Discard your changes and reload?");
            if (discard)
            {
                await ApplyPersistedSnapshotAsync(snapshot);
            }
            else
            {
                // On no: keep editing and hold BaselineUpdated back so the next save's
                // optimistic-concurrency check still observes the external metadata edit.
                // The pin survives later derived-only emissions until the conflict is
                // resolved (reload, save, or absorb).
                metadataConflictPending = true;
            }

            return;
        }

        if (metadataChanged)
        {
            // External metadata change with no local edits: absorb it (reset the
            // editable fields and advance the baseline).
            await ApplyPersistedSnapshotAsync(snapshot);
            if (reloadTelemetry)
            {
                await RequestLoadAsync();
            }

            return;
        }

        // Pure derived change (recompute, GPS offset, sync blob swap): refresh the
        // telemetry/track and advance the baseline without resetting editable fields.
        // While an external metadata edit is unacknowledged, keep the baseline pinned
        // so the next save still detects that conflict instead of overwriting it.
        RefreshDerivedState(snapshot);
        if (!metadataConflictPending)
        {
            BaselineUpdated = snapshot.Updated;
        }

        if (reloadTelemetry)
        {
            await RequestLoadAsync();
        }
    }

    private void RefreshDerivedState(SessionSnapshot snapshot)
    {
        session.FullTrack = snapshot.FullTrackId;
        session.GpsOffsetSeconds = snapshot.GpsOffsetSeconds;
        session.Updated = snapshot.Updated;
        IsComplete = snapshot.HasProcessedData;
        SessionContext.SessionSnapshot = snapshot;
        UpdateRecordedSessionExtensionHostState();
    }

    private Task HandleDeferredDomainAsync()
    {
        if (!viewLoaded || deferredDomain is not { } domain)
        {
            return Task.CompletedTask;
        }

        deferredDomain = null;
        return OnDomainChangedAsync(domain);
    }

    private RecordedSessionHostState CreateRecordedSessionExtensionHostState()
    {
        var snapshot = sessionStore.Get(Id);
        var timelineDurationSeconds = SessionContext.TelemetryData?.Metadata.Duration ?? snapshot?.DurationSeconds;

        return new RecordedSessionHostState(
            new RecordedSessionIdentityState(
                Id,
                snapshot?.Name,
                snapshot?.Timestamp,
                snapshot?.DurationSeconds,
                viewLoaded,
                IsTabActive),
            new RecordedSessionSelectionState(SessionContext.AnalysisRange),
            new RecordedSessionTimelineState(
                SessionContext.TrackTimelineContext,
                timelineDurationSeconds,
                Timeline,
                new RecordedSessionTimelineAlignmentState(pendingTimelineAlignmentMark)),
            new RecordedSessionStatisticsState(
                SessionContext.DamperPercentages,
                SessionContext.DampingSpeedCutoffs,
                SessionContext.SelectedVelocityAverageMode,
                SessionContext.SelectedTravelHistogramMode));
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

    private static bool CanUseTimelineAlignmentMark(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds)
    {
        return target is not RecordedSessionTimelineAlignmentTarget.None &&
               double.IsFinite(seconds) &&
               seconds >= 0;
    }

    private bool IsPendingTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        string? subjectId)
    {
        return pendingTimelineAlignmentMark is { } pendingMark &&
               pendingMark.Target == target &&
               string.Equals(pendingMark.SubjectId, subjectId, StringComparison.Ordinal);
    }

    private void NotifyTimelineAlignmentCommandsCanExecuteChanged()
    {
        markGpsEventCommand.NotifyCanExecuteChanged();
        markGpsTelemetryEventCommand.NotifyCanExecuteChanged();
    }

    private static bool IsTelemetryPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return context is { ClickSeconds: >= 0 } &&
               double.IsFinite(context.ClickSeconds) &&
               context.RowId is TelemetryGraphRowIds.Travel or
                   TelemetryGraphRowIds.Velocity or
                   TelemetryGraphRowIds.Imu or
                   TelemetryGraphRowIds.PitchRoll or
                   TelemetryGraphRowIds.Speed or
                   TelemetryGraphRowIds.Elevation;
    }

    private bool CanMarkGpsEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is null &&
               SessionContext.TelemetryData is not null &&
               SessionContext.TrackPoints is { Count: > 0 } &&
               IsTelemetryPlotContext(context);
    }

    private void MarkGpsEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkGpsEventFromPlotContext(context))
        {
            return;
        }

        _ = ((IRecordedSessionHostOperations)this).TryBeginTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.GpsTrack,
            context!.ClickSeconds);
    }

    private bool CanMarkGpsTelemetryEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return SessionContext.TelemetryData is not null &&
               IsPendingTimelineAlignment(RecordedSessionTimelineAlignmentTarget.GpsTrack, subjectId: null) &&
               IsTelemetryPlotContext(context);
    }

    private async Task MarkGpsTelemetryEventFromPlotContextAsync(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkGpsTelemetryEventFromPlotContext(context) ||
            !((IRecordedSessionHostOperations)this).TryResolveTimelineAlignment(
                RecordedSessionTimelineAlignmentTarget.GpsTrack,
                context!.ClickSeconds,
                subjectId: null,
                out var resolution) ||
            resolution is null ||
            SessionContext.TelemetryData is not { } telemetry)
        {
            return;
        }

        var newOffsetSeconds = NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds + resolution.OffsetDeltaSeconds);
        var applied = await trackCoordinator.UpdateSessionGpsOffsetAsync(
            Id,
            session.FullTrack,
            telemetry,
            newOffsetSeconds);

        if (!applied)
        {
            ErrorMessages.Add("GPS offset could not be applied: no matching track segment was found.");
        }

        // One-way: on success the store upsert drives the refreshed track and
        // baseline through the session-detail watch reaction, like recompute.
    }

    private static double NormalizeGpsOffsetSeconds(double gpsOffsetSeconds) =>
        double.IsFinite(gpsOffsetSeconds) ? gpsOffsetSeconds : 0;

    void IRecordedSessionHostOperations.SetTimelineVisibleRange(
        double startNormalized,
        double endNormalized,
        object source) =>
        SetRecordedSessionExtensionTimelineVisibleRange(startNormalized, endNormalized, source);

    bool IRecordedSessionHostOperations.TryBeginTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId)
    {
        if (!CanUseTimelineAlignmentMark(target, seconds) ||
            pendingTimelineAlignmentMark is not null)
        {
            return false;
        }

        pendingTimelineAlignmentMark = new RecordedSessionTimelineAlignmentMark(target, subjectId, seconds);
        UpdateRecordedSessionExtensionHostState();
        NotifyTimelineAlignmentCommandsCanExecuteChanged();
        return true;
    }

    bool IRecordedSessionHostOperations.TryResolveTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId,
        out RecordedSessionTimelineAlignmentResolution? resolution)
    {
        resolution = null;
        if (!CanUseTimelineAlignmentMark(target, seconds) ||
            !IsPendingTimelineAlignment(target, subjectId) ||
            pendingTimelineAlignmentMark is not { } pendingMark)
        {
            return false;
        }

        resolution = new RecordedSessionTimelineAlignmentResolution(
            target,
            pendingMark.SubjectId,
            pendingMark.Seconds,
            seconds,
            pendingMark.Seconds - seconds);
        pendingTimelineAlignmentMark = null;
        UpdateRecordedSessionExtensionHostState();
        NotifyTimelineAlignmentCommandsCanExecuteChanged();
        return true;
    }

    bool IRecordedSessionHostOperations.TryCancelTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        string? subjectId)
    {
        if (!IsPendingTimelineAlignment(target, subjectId))
        {
            return false;
        }

        pendingTimelineAlignmentMark = null;
        UpdateRecordedSessionExtensionHostState();
        NotifyTimelineAlignmentCommandsCanExecuteChanged();
        return true;
    }

    void IRecordedSessionHostOperations.AddError(string message) => ErrorMessages.Add(message);

    void IRecordedSessionHostOperations.AddNotification(string message) => Notifications.Add(message);

    IRecordedSessionOperationLease IRecordedSessionHostOperations.StartOperation(string description) =>
        (recordedSessionOperationCoordinator
            ?? throw new InvalidOperationException("Recorded-session extension hosting is not configured."))
        .StartOperation(description);

    void IRecordedSessionHostOperations.RequestPageSelection(string contributionId) =>
        extensionPagesController?.RequestRecordedSessionExtensionPageSelection(contributionId);

    Guid ISessionOperationGateway.SessionId => Id;

    bool ISessionOperationGateway.IsDirty => IsDirty;

    bool ISessionOperationGateway.IsViewLoaded => viewLoaded;

    bool ISessionOperationGateway.ShouldDeferDomainHandling() => ShouldDeferDomainHandling();

    void ISessionOperationGateway.UpdateExtensionHostState() => UpdateRecordedSessionExtensionHostState();

    void ISessionOperationGateway.SetGraphPreferences(SessionGraphPreferences preferences) =>
        GraphPreferences = preferences;

    #endregion

    #region Constructors

    internal SessionDetailViewModel(
        SessionSnapshot snapshot,
        ISessionCoordinator sessionCoordinator,
        ITrackCoordinator trackCoordinator,
        ISessionStore sessionStore,
        IRecordedSessionGraph recordedSessionGraph,
        ISessionPresentationService sessionPresentationService,
        ISessionAnalysisService sessionAnalysisService,
        IMapViewModelFactory mapViewModelFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISessionPreferences sessionPreferences,
        IUiThreadDispatcher uiThreadDispatcher,
        ISessionLayoutStrategy layoutStrategy,
        IRecordedSessionProcessingOptionCache recordedSessionProcessingOptionCache,
        IBikeCoordinator? bikeCoordinator = null,
        ExtensionHostDependencies? extensionHost = null)
        : base(shell, dialogService, uiThreadDispatcher)
    {
        ArgumentNullException.ThrowIfNull(sessionPreferences);
        this.layoutStrategy = layoutStrategy;

        this.sessionCoordinator = sessionCoordinator;
        this.trackCoordinator = trackCoordinator;
        this.bikeCoordinator = bikeCoordinator;
        this.sessionStore = sessionStore;
        this.recordedSessionGraph = recordedSessionGraph;
        this.sessionPresentationService = sessionPresentationService;
        this.sessionAnalysisService = sessionAnalysisService;
        this.recordedSessionProcessingOptionCache = recordedSessionProcessingOptionCache;
        recordedPreferenceStore = new RecordedPreferenceStore(
            sessionPreferences,
            () => Id,
            ErrorMessages.Add);
        plotRowActions = new SessionPlotRowActionsController(SessionContext);
        plotAutozoomController = new PlotAutozoomController(Timeline);
        markGpsEventCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            MarkGpsEventFromPlotContext,
            CanMarkGpsEventFromPlotContext);
        markGpsTelemetryEventCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            MarkGpsTelemetryEventFromPlotContextAsync,
            CanMarkGpsTelemetryEventFromPlotContext);
        PlotContextMenuActionsByRowId = CreatePlotContextMenuActionsByRowId(
            plotAutozoomController.ActionsByRowId,
            markGpsEventCommand,
            markGpsTelemetryEventCommand);
        SessionContext.PlotContextMenuActionsByRowId = PlotContextMenuActionsByRowId;
        session = SessionFromSnapshot(snapshot);
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        SessionContext.SessionSnapshot = snapshot;
        MobileWorkspace = new SessionShellMobileWorkspaceViewModel(this, SessionContext);
        GraphWorkspace = new RecordedSessionGraphWorkspaceViewModel(
            SessionContext,
            this);
        MediaWorkspace = new SessionMediaWorkspaceViewModel(SessionContext);
        StatisticsWorkspace = new SessionStatisticsWorkspaceViewModel(
            SessionContext,
            this,
            SelectTelemetryRangeSelectionCommand);
        SidebarWorkspace = new SessionSidebarWorkspaceViewModel(
            this,
            () => Name,
            value => Name = value,
            NotesPage,
            PreferencesPage,
            SaveCommand,
            ResetCommand);
        IsComplete = snapshot.HasProcessedData;
        damperCutoffWorkflow = new DamperCutoffWorkflow(
            SessionContext,
            bikeCoordinator,
            ErrorMessages.Add);
        stalenessReconciler = new SessionStalenessReconciler(
            sessionCoordinator,
            dialogService,
            this);
        processingPreferenceWorkflow = new ProcessingPreferenceWorkflow(
            recordedPreferenceStore,
            PreferencesPage,
            sessionCoordinator,
            recordedSessionProcessingOptionCache,
            this);
        if (extensionHost is not null)
        {
            recordedSessionOperationCoordinator = new RecordedSessionOperationCoordinator(
                ReportRecordedSessionExtensionOperation,
                CompleteRecordedSessionExtensionOperation);
            recordedSessionExtensions = new RecordedSessionExtensionManager(
                Id,
                extensionHost.RecordedSessionExtensionFactories,
                extensionHost.ExtensionDatabase,
                extensionHost.RecordedSessionDataReader,
                extensionHost.BackgroundTaskRunner,
                uiThreadDispatcher,
                recordedSessionOperationCoordinator,
                this);
            extensionPagesController = new RecordedSessionExtensionPagesController(recordedSessionExtensions, SessionContext);
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
        SessionContext.MapViewModel = mapViewModelFactory.Create();
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
            GpsOffsetSeconds = snapshot.GpsOffsetSeconds,
            Updated = snapshot.Updated,
        };
        return s;
    }

    private void ClearStatisticsSelections()
    {
        statisticsSelectionController.Clear();
        SyncStatisticsSelectionController();
        plotRowActions.ClearStatisticsSelectionToggles();
        plotRowActions.RefreshStatisticsSelectionActionStates();
    }

    private void ClearDampingRangeSelections()
    {
        if (!statisticsSelectionController.ClearDampingRangeSelections(SessionContext.TelemetryData, SessionContext.AnalysisRange))
        {
            return;
        }

        SyncStatisticsSelectionController();
        if (!SessionContext.HasStatisticsSelection)
        {
            plotRowActions.ClearStatisticsSelectionToggles();
        }

        plotRowActions.RefreshStatisticsSelectionActionStates();
    }

    private void SyncStatisticsSelectionController()
    {
        SessionContext.SelectedFrontRangeSelection = statisticsSelectionController.SelectedFrontRangeSelection;
        SessionContext.SelectedRearRangeSelection = statisticsSelectionController.SelectedRearRangeSelection;
        OnPropertyChanged(nameof(SelectedFrontRangeSelection));
        OnPropertyChanged(nameof(SelectedRearRangeSelection));
        SessionContext.StatisticsSelectionHighlightRanges = statisticsSelectionController.HighlightRanges;
    }

    private void OnSessionContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(RecordedSessionContext.TelemetryData):
                IsComplete = SessionContext.TelemetryData != null;
                NotesPage.SetTemperatureAverages(SessionContext.TelemetryData?.TemperatureAverages ?? []);
                pendingAnalysisRangeBoundary = null;
                ClearStatisticsSelections();
                RefreshTrackTimelineContext();
                NotifyTimelineAlignmentCommandsCanExecuteChanged();
                if (SessionContext.TelemetryData is null)
                {
                    SessionContext.SessionAnalysis = SessionAnalysisResult.Hidden;
                    UpdateRecordedSessionExtensionHostState();
                    break;
                }

                if (SessionContext.AnalysisRange is not null)
                {
                    ClearAnalysisRange();
                    break;
                }

                RecomputeDamperPercentagesForAnalysisRange();
                RecomputeSessionAnalysisIfAllowed();
                UpdateRecordedSessionExtensionHostState();
                break;
            case nameof(RecordedSessionContext.AnalysisRange):
                OnPropertyChanged(nameof(SessionAnalysisRangeText));
                ClearStatisticsSelections();
                presentationApplier.RefreshAnalysisRangeStates();
                RecomputeDamperPercentagesForAnalysisRange();
                RecomputeSessionAnalysisIfAllowed();
                UpdateRecordedSessionExtensionHostState();
                break;
            case nameof(RecordedSessionContext.SelectedTravelHistogramMode):
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                RecomputeSessionAnalysis();
                PersistRecordedStatisticsPreferencesIfEnabled();
                UpdateRecordedSessionExtensionHostState();
                break;
            case nameof(RecordedSessionContext.SelectedBalanceDisplacementMode):
            case nameof(RecordedSessionContext.SelectedBalanceSpeedMode):
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                RecomputeSessionAnalysis();
                PersistRecordedStatisticsPreferencesIfEnabled();
                break;
            case nameof(RecordedSessionContext.SelectedVelocityAverageMode):
                ClearDampingRangeSelections();
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                RecomputeDamperPercentagesForAnalysisRange();
                RecomputeSessionAnalysis();
                PersistRecordedStatisticsPreferencesIfEnabled();
                UpdateRecordedSessionExtensionHostState();
                break;
            case nameof(RecordedSessionContext.SelectedSessionAnalysisTargetProfile):
                RecomputeSessionAnalysis();
                PersistRecordedStatisticsPreferencesIfEnabled();
                break;
            case nameof(RecordedSessionContext.DampingSpeedCutoffs):
                RecomputeDamperPercentagesForAnalysisRange();
                RecomputeSessionAnalysisIfAllowed();
                UpdateRecordedSessionExtensionHostState();
                break;
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
                NotifyTimelineAlignmentCommandsCanExecuteChanged();
                if (SessionContext.TelemetryData is not null)
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
        }
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
        LayoutPreferences = preferences.Layout;
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
            SessionContext.SelectedTravelHistogramMode = preferences.TravelHistogramMode;
            SessionContext.SelectedVelocityAverageMode = preferences.VelocityAverageMode;
            SessionContext.SelectedBalanceDisplacementMode = preferences.BalanceDisplacementMode;
            SessionContext.SelectedBalanceSpeedMode = preferences.BalanceSpeedMode;
            SessionContext.SelectedSessionAnalysisTargetProfile = preferences.SessionAnalysisTargetProfile;
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
            SessionContext.SelectedTravelHistogramMode,
            SessionContext.SelectedVelocityAverageMode,
            SessionContext.SelectedBalanceDisplacementMode,
            SessionContext.SelectedBalanceSpeedMode,
            SessionContext.SelectedSessionAnalysisTargetProfile);
    }

    private void PersistRecordedStatisticsPreferencesIfEnabled()
    {
        var statistics = CreateStatisticsPreferences();
        recordedPreferenceStore.UpdateCurrent(current => current with { Statistics = statistics });
        recordedPreferenceStore.PersistChangeIfEnabled(current => current with { Statistics = statistics });
    }

    private void OnProcessingPreferenceChangeCommitted(object? sender, EventArgs args) =>
        _ = processingPreferenceWorkflow.HandleProcessingPreferenceChangeCommittedAsync();

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
            GpsOffsetSeconds = session.GpsOffsetSeconds,
        };

        var result = await sessionCoordinator.SaveAsync(newSession, BaselineUpdated);
        switch (result)
        {
            case SessionSaveResult.Saved saved:
                session = newSession;
                session.Updated = saved.NewBaselineUpdated;
                BaselineUpdated = saved.NewBaselineUpdated;
                metadataConflictPending = false;
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
                    metadataConflictPending = false;
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
        if (!statisticsSelectionController.Select(selection, SessionContext.TelemetryData, SessionContext.AnalysisRange)) return;

        SyncStatisticsSelectionController();
        plotRowActions.ClearStatisticsSelectionToggles();
        if (SessionContext.HasStatisticsSelection)
        {
            SessionContext.ShowStatisticsSelection = true;
        }

        plotRowActions.RefreshStatisticsSelectionActionStates();
    }

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        pendingAnalysisRangeBoundary = null;
        if (SessionContext.TelemetryData is null ||
            !TelemetryTimeRange.TryCreateClamped(
                startSeconds,
                endSeconds,
                SessionContext.TelemetryData.Metadata.Duration,
                out var range))
        {
            return;
        }

        SessionContext.AnalysisRange = range;
    }

    public void ClearAnalysisRange()
    {
        pendingAnalysisRangeBoundary = null;
        if (SessionContext.AnalysisRange is null)
        {
            return;
        }

        SessionContext.AnalysisRange = null;
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        if (SessionContext.TelemetryData is null ||
            !TelemetryTimeRange.TryClampBoundary(boundarySeconds, SessionContext.TelemetryData.Metadata.Duration, out var clampedBoundarySeconds))
        {
            pendingAnalysisRangeBoundary = null;
            return;
        }

        if (SessionContext.AnalysisRange is { } range)
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
            s.Add(recordedPreferenceStore.Observe().Subscribe(OnSyncedPreferencesArrived));
            s.Add(watch.Subscribe(domain => _ = OnDomainChangedAsync(domain)));
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

        _ = HandleDeferredDomainAsync();
    }

    protected override void OnDeactivated()
    {
        UpdateRecordedSessionExtensionHostState();
    }

    private bool ShouldDeferDomainHandling() =>
        layoutStrategy.DefersDomainHandlingWhenInactive &&
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
        observedInitialDomain = false;
        deferredDomain = null;
        stalenessReconciler.ResetForUnload();
        UpdateRecordedSessionExtensionHostState();
        await DisposeRecordedSessionExtensionScopesAsync();
        DisposeScopedSubscriptions();
    }

    #endregion

}
