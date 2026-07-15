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
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Media.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Insights.ViewModels.Editors;
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
    public ISessionShellMobileWorkspace MobileWorkspace { get; }
    public IRecordedSessionSignalsWorkspace SignalsWorkspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }
    public ISessionAnalysisWorkspace AnalysisWorkspace { get; }
    public ISessionSidebarWorkspace SidebarWorkspace { get; }
    public SessionTimelineLinkViewModel Timeline => timeline;

    #region Private fields

    private readonly ISessionCoordinator sessionCoordinator;
    private readonly ITrackCoordinator trackCoordinator;
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly ISessionStore sessionStore;
    private readonly IRecordedSessionProjection recordedSessionProjection;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly ObservableCollection<PageViewModelBase> pages = [];
    private readonly SessionTimelineLinkViewModel timeline = new();
    private readonly TelemetrySourceVisibilityStore sourceVisibility = new();
    private readonly RecordedSessionExtensionSlots emptyExtensionSlots = new();
    private readonly RecordedSessionExtensionManager? recordedSessionExtensions;
    private readonly RecordedSessionOperationCoordinator? recordedSessionOperationCoordinator;
    private readonly SessionStalenessReconciler stalenessReconciler;
    private readonly IRecordedSessionProcessingOptionCache recordedSessionProcessingOptionCache;
    private readonly IRecordedSessionAnalysisResultState analysisResultState;
    private readonly IDisposable analysisResultSubscription;
    private readonly RecordedSessionEditorActions editorActions = new();
    private readonly IDisposable editorStateSubscription;
    private readonly RecordedSessionEditorEffects editorEffects;
    private readonly Subject<int> pageCountInput = new();
    private readonly Subject<SessionPreferences> preferenceReplayInput = new();
    private readonly Subject<RecordedSessionLoadPresentation> loadPresentationInput = new();
    private readonly Subject<SessionOperationPresentationState> sessionOperationStateInput = new();
    private readonly Subject<SurfacePresentationState> mediaPaneStateInput = new();
    private readonly Subject<string?> mediaUrlInput = new();
    private readonly Subject<SessionDampingPercentages> dampingPercentagesInput = new();
    private readonly Subject<DampingSpeedCutoffs> plotDampingSpeedCutoffsInput = new();
    private readonly Subject<bool> canEditDampingSpeedCutoffsInput = new();
    private readonly Subject<SessionInsightsResult> sessionInsightsInput = new();
    private readonly Subject<AnalysisSelectionState> analysisSelectionInput = new();
    private readonly Subject<RecordedSessionHostRuntimeState> hostRuntimeInput = new();
    private readonly Subject<RecordedSessionDomainSnapshot> domainInput = new();
    private readonly Subject<Unit> stalenessReplayInput = new();
    private readonly Subject<Unit> dirtyBaselineInput = new();
    private readonly Subject<IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>> signalPlotContextMenuActionsInput = new();
    private readonly CompositeDisposable editorInputSubjects;
    private readonly RecordedSessionEditorStateController editorStateController;
    private RecordedSessionEditorState currentEditorState = RecordedSessionEditorState.CreateInitial();
    private readonly IRecordedSessionDerivationWindowCache recordedSessionDerivationWindowCache;
    private readonly Func<IEditorFactory> editorFactory;
    private readonly ILayoutProfileTransitionState layoutProfileTransitionState;
    private bool observedInitialDomain;
    private bool replayStalenessOnNextLoad;
    private RecordedSessionDomainSnapshot? deferredDomain;
    private readonly RecordedPagePresentationApplier presentationApplier;
    private readonly RecordedPreferenceStore recordedPreferenceStore;
    private readonly RecordedSessionExtensionPagesController? extensionPagesController;
    private readonly ProcessingPreferenceWorkflow processingPreferenceWorkflow;
    private Session session;
    private RecordedSignalsPageViewModel SignalsPage { get; }
    private StrokesPageViewModel StrokesPage { get; }
    private SpringPageViewModel SpringPage { get; }
    private BalancePageViewModel BalancePage { get; }
    private VibrationPageViewModel VibrationPage { get; }
    private SessionInsightsPageViewModel AnalysisPage { get; }

    private readonly CancellableOperation loadOperation = new();
    private readonly CancellableOperation viewLoadOperation = new();
    private SessionPresentationDimensions? lastPresentationDimensions;
    private RecordedSessionTimelineAlignmentMark? pendingTimelineAlignmentMark;
    private RecordedSessionAnalysisInputs analysisInputs;
    private readonly AnalysisRequestScheduler analysisRequestScheduler;
    private int telemetryGeneration;
    private bool suppressDirtinessEvaluation;
    private bool suppressInsightsRecompute;
    private bool suppressAnalysisRecompute;
    // Set when the user declines to reload after an external metadata edit landed
    // on a dirty draft: BaselineUpdated is pinned below that edit so the next save
    // still conflicts. A later derived-only emission must not advance the baseline
    // past the unacknowledged edit, or the conflict would be silently lost.
    private bool metadataConflictPending;
    private bool viewLoaded;
    private bool hasBeenActivated;
    private bool editorInputSubjectsDisposed;
    private readonly SignalRowActionsController signalRowActions;
    private readonly SignalAutozoomController signalAutozoomController;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> setAnalysisRangeStartCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> setAnalysisRangeEndCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> clearAnalysisRangeFromContextCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> markGpsEventCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markGpsTelemetryEventCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> cancelGpsTimelineAlignmentCommand;
    private readonly DampingCutoffWorkflow dampingCutoffWorkflow;
    private readonly bool deferDomainHandlingWhenInactive;
    private IDisposable? processedTelemetryRetention;
    private SessionSnapshot? sessionSnapshot;
    private TelemetryData? telemetryData;
    private List<TrackPoint>? fullTrackPoints;
    private List<TrackPoint>? trackPoints;
    private MapViewModel? mapViewModel;
    private bool mapInitializeRequested;

    #endregion Private fields

    #region Public fields

    public DampingPageViewModel DampingPage { get; }
    public NotesPageViewModel NotesPage { get; } = new();
    public SignalDisplayPreferences SignalDisplayPreferences
    {
        get => currentEditorState.Intent.SignalDisplayPreferences;
    }

    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => currentEditorState.Intent.SignalLayoutPreferences;
        set => editorActions.SetSignalLayoutPreferences(value);
    }

    public SessionLayoutPreferences LayoutPreferences
    {
        get => currentEditorState.Intent.LayoutPreferences;
        set => editorActions.SetLayoutPreferences(value);
    }

    public SessionPaneGroupPreferences? MediaLayoutPreferences
    {
        get => currentEditorState.Intent.LayoutPreferences.DesktopMediaRows;
        set => LayoutPreferences = LayoutPreferences with { DesktopMediaRows = value };
    }

    public TelemetrySourceVisibilityStore SourceVisibility => sourceVisibility;
    public PreferencesPageViewModel PreferencesPage { get; } = new();
    public MapViewModel? MapViewModel => mapViewModel;
    public IReadOnlyList<SignalRowAction> TravelHeaderActions => signalRowActions.TravelHeaderActions;
    public IReadOnlyList<SignalRowAction> VelocityHeaderActions => signalRowActions.VelocityHeaderActions;
    public IReadOnlyList<SignalRowAction> ImuHeaderActions => signalRowActions.ImuHeaderActions;
    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions => signalRowActions.PitchRollHeaderActions;
    public IReadOnlyList<SignalRowAction> SpeedHeaderActions => signalRowActions.SpeedHeaderActions;
    public IReadOnlyList<SignalRowAction> ElevationHeaderActions => signalRowActions.ElevationHeaderActions;
    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => currentEditorState.AnalysisSelection.ActiveFront;
    public TelemetryRangeSelection? ActiveRearAnalysisSelection => currentEditorState.AnalysisSelection.ActiveRear;
    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; }
    public bool CanEditDampingSpeedCutoffs => currentEditorState.Presentation.CanEditDampingSpeedCutoffs;
    public RecordedSessionExtensionSlots ExtensionSlots => recordedSessionExtensions?.ExtensionSlots ?? emptyExtensionSlots;

    #endregion Public fields

    #region Observable properties

    [ObservableProperty] public partial bool IsComplete { get; set; }
    public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } = SessionInsightsPresentation.TravelDistributionModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionInsightsPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionInsightsPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionInsightsPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } = SessionInsightsPresentation.SessionInsightsTargetProfileOptions;
    public string SessionAnalysisRangeText => currentEditorState.Intent.AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";
    public string SessionAnalysisModesText => SessionInsightsPresentation.DescribeModes(
        currentEditorState.Intent.SelectedTravelDistributionMode,
        currentEditorState.Intent.SelectedVelocityAverageMode,
        currentEditorState.Intent.SelectedBalanceDisplacementMode,
        currentEditorState.Intent.SelectedBalanceSpeedMode);
    public ObservableCollection<PageViewModelBase> Pages => pages;
    public SessionScreenPresentationState ScreenState => currentEditorState.Presentation.ScreenState;
    public SessionOperationPresentationState SessionOperationState => currentEditorState.Presentation.OperationState;

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

    internal void ApplyDampingPercentages(SessionDampingPercentages percentages)
    {
        DampingPage.ApplyDampingPercentages(percentages);
        PublishEditorInput(dampingPercentagesInput, percentages);
    }

    internal void ApplyDampingSpeedCutoffContext(
        DampingSpeedCutoffs cutoffs,
        DampingSpeedCutoffOwner? owner)
    {
        dampingCutoffWorkflow.ApplyContext(cutoffs, owner);
    }

    public void PreviewDampingSpeedCutoff(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond) =>
        dampingCutoffWorkflow.Preview(side, circuit, cutoffMmPerSecond);

    public void CancelDampingSpeedCutoffPreview() => dampingCutoffWorkflow.CancelPreview();

    public Task CommitDampingSpeedCutoffAsync(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond) =>
        dampingCutoffWorkflow.CommitAsync(side, circuit, cutoffMmPerSecond);

    private void ClearDampingPercentages()
    {
        ApplyDampingPercentages(SessionDampingPercentages.Empty);
    }

    private RecordedSessionAnalysisInputs CreateCurrentAnalysisInputs() =>
        new(
            telemetryGeneration,
            currentEditorState.Intent.AnalysisRange,
            currentEditorState.Intent.SelectedTravelDistributionMode,
            currentEditorState.Intent.SelectedVelocityAverageMode,
            currentEditorState.Intent.SelectedBalanceDisplacementMode,
            currentEditorState.Intent.SelectedBalanceSpeedMode,
            currentEditorState.Intent.DampingSpeedCutoffs,
            currentEditorState.Presentation.DampingPercentages,
            currentEditorState.Intent.SelectedSessionInsightsTargetProfile);

    private void InvalidateAnalysisInputs()
    {
        analysisRequestScheduler.InvalidateInputs();
    }

    private void RequestAnalysisResult(RecordedSessionAnalysisKey key)
    {
        if (analysisResultState.Get(key) is { } cached)
        {
            ApplyAnalysisResult(key, cached);
            return;
        }

        _ = analysisResultState.RequestAsync(key);
    }

    private void RequestCurrentDampingPercentages()
    {
        analysisRequestScheduler.RequestDamping(includeInsights: false, respectSuppression: false);
    }

    private void RequestCurrentSessionInsights(bool respectSuppression = false)
    {
        analysisRequestScheduler.RequestInsights(respectSuppression);
    }

    private void RequestCurrentAnalysisResults(bool includeInsights, bool respectSuppression = false)
    {
        analysisRequestScheduler.RequestDamping(includeInsights, respectSuppression);
    }

    private sealed class AnalysisRequestScheduler(
        SessionDetailViewModel owner,
        RecordedSessionAnalysisInputs initialInputs)
    {
        private readonly Stack<bool> batchSuppressionStack = [];
        private RecordedSessionAnalysisInputs currentInputs = initialInputs;
        private bool pendingDampingRequest;
        private bool pendingInsightsRequest;
        private bool pendingTelemetryInsightsRequest;
        private bool requestInsightsAfterDamping;
        private bool suppressedInsightsDuringBatch;

        public void BeginBatch(bool suppressInsights)
        {
            if (batchSuppressionStack.Count == 0)
            {
                suppressedInsightsDuringBatch = false;
            }

            batchSuppressionStack.Push(suppressInsights);
        }

        public void EndBatch()
        {
            if (batchSuppressionStack.Count == 0)
            {
                return;
            }

            _ = batchSuppressionStack.Pop();
            if (batchSuppressionStack.Count > 0)
            {
                return;
            }

            if (suppressedInsightsDuringBatch && owner.IsSessionInsightsPageSelected)
            {
                pendingInsightsRequest = true;
            }

            suppressedInsightsDuringBatch = false;
            FlushIfAllowed();
        }

        public void InvalidateInputs()
        {
            var nextInputs = owner.CreateCurrentAnalysisInputs();
            if (nextInputs == currentInputs)
            {
                return;
            }

            currentInputs = nextInputs;
            owner.analysisInputs = nextInputs;
            owner.analysisResultState.Invalidate(nextInputs);
            requestInsightsAfterDamping = false;
        }

        public void RequestDamping(bool includeInsights, bool respectSuppression)
        {
            InvalidateInputs();
            pendingDampingRequest = true;
            if (includeInsights && AllowInsights(respectSuppression))
            {
                pendingInsightsRequest = true;
            }

            FlushIfAllowed();
        }

        public void RequestInsights(bool respectSuppression)
        {
            InvalidateInputs();
            if (!AllowInsights(respectSuppression))
            {
                return;
            }

            pendingInsightsRequest = true;
            FlushIfAllowed();
        }

        public void OnTelemetryUnavailable()
        {
            pendingDampingRequest = false;
            pendingInsightsRequest = false;
            pendingTelemetryInsightsRequest = false;
            requestInsightsAfterDamping = false;
            InvalidateInputs();
            owner.SetSessionInsights(SessionInsightsResult.Hidden);
        }

        public bool ConsumePendingTelemetryInsightsRequest()
        {
            var shouldRequest = pendingTelemetryInsightsRequest;
            pendingTelemetryInsightsRequest = false;
            return shouldRequest;
        }

        public void OnDampingPercentagesApplied()
        {
            if (!requestInsightsAfterDamping)
            {
                return;
            }

            requestInsightsAfterDamping = false;
            InvalidateInputs();
            pendingInsightsRequest = true;
            FlushIfAllowed();
        }

        private bool AllowInsights(bool respectSuppression)
        {
            if (respectSuppression &&
                batchSuppressionStack.Any(static suppressInsights => suppressInsights))
            {
                suppressedInsightsDuringBatch = true;
                return false;
            }

            return true;
        }

        private void FlushIfAllowed()
        {
            if (batchSuppressionStack.Count > 0)
            {
                return;
            }

            if (owner.telemetryData is null)
            {
                if (pendingDampingRequest)
                {
                    pendingDampingRequest = false;
                    owner.ClearDampingPercentages();
                }

                if (pendingInsightsRequest)
                {
                    pendingInsightsRequest = false;
                    pendingTelemetryInsightsRequest = true;
                    owner.SetSessionInsights(SessionInsightsResult.Hidden);
                }

                return;
            }

            if (pendingDampingRequest)
            {
                pendingDampingRequest = false;
                requestInsightsAfterDamping = pendingInsightsRequest;
                pendingInsightsRequest = false;
                owner.RequestAnalysisResult(currentInputs.DampingPercentagesKey);
                return;
            }

            if (pendingInsightsRequest)
            {
                pendingInsightsRequest = false;
                pendingTelemetryInsightsRequest = false;
                owner.RequestAnalysisResult(currentInputs.SessionInsightsKey);
            }
        }
    }

    private void OnAnalysisResultChanged(RecordedSessionAnalysisResultChanged change)
    {
        if (!change.Key.Matches(analysisInputs))
        {
            return;
        }

        if (change.Error is not null)
        {
            ErrorMessages.Add($"Failed to update session analysis: {change.Error.Message}");
            return;
        }

        if (change.Result is not null)
        {
            ApplyAnalysisResult(change.Key, change.Result);
        }
    }

    private void ApplyAnalysisResult(
        RecordedSessionAnalysisKey key,
        RecordedSessionAnalysisResult result)
    {
        if (!key.Matches(analysisInputs))
        {
            return;
        }

        switch (result)
        {
            case DampingPercentagesAnalysisResult damping:
                ApplyDampingPercentages(damping.Percentages);
                analysisRequestScheduler.OnDampingPercentagesApplied();

                break;
            case SessionInsightsAnalysisResult insights:
                SetSessionInsights(insights.Insights);
                break;
        }
    }

    internal void RecomputeSessionInsights()
    {
        editorActions.RequestSessionInsights();
    }

    private PageViewModelBase? SelectedPage => Pages.Count == 0
        ? null
        : Pages[RecordedSessionPageSelection.ClampSelectedPageIndex(
            currentEditorState.Intent.SelectedPageIndex,
            Pages.Count)];

    private bool IsSessionInsightsPageSelected => ReferenceEquals(SelectedPage, AnalysisPage);

    internal SessionSnapshot? CurrentSessionSnapshot => currentEditorState.Session;

    internal TelemetryData? CurrentTelemetryData => currentEditorState.TelemetryData;

    internal IReadOnlyList<TrackPoint>? CurrentTrackPoints => currentEditorState.TrackPoints;

    internal TelemetryTimeRange? CurrentAnalysisRange => currentEditorState.Intent.AnalysisRange;

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreateSignalPlotContextMenuActionsBySignalRowId(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        IRelayCommand<TelemetryPlotContextMenuContext?> setAnalysisRangeStartCommand,
        IRelayCommand<TelemetryPlotContextMenuContext?> setAnalysisRangeEndCommand,
        IRelayCommand<TelemetryPlotContextMenuContext?> clearAnalysisRangeCommand,
        IRelayCommand<TelemetryPlotContextMenuContext?> markGpsEventCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markGpsTelemetryEventCommand,
        IRelayCommand<TelemetryPlotContextMenuContext?> cancelGpsTimelineAlignmentCommand)
    {
        var setAnalysisRangeStart = new TelemetryPlotContextMenuAction(
            "analysis-range-set-start",
            "Set analysis start here",
            setAnalysisRangeStartCommand);
        var setAnalysisRangeEnd = new TelemetryPlotContextMenuAction(
            "analysis-range-set-end",
            "Set analysis end here",
            setAnalysisRangeEndCommand);
        var clearAnalysisRange = new TelemetryPlotContextMenuAction(
            "analysis-range-clear",
            "Clear analysis range",
            clearAnalysisRangeCommand);
        var markGpsEvent = new TelemetryPlotContextMenuAction(
            "gps-mark-gps-event",
            "Mark GPS event here",
            markGpsEventCommand);
        var markGpsTelemetryEvent = new TelemetryPlotContextMenuAction(
            "gps-mark-telemetry-event",
            "Mark telemetry event here",
            markGpsTelemetryEventCommand);
        var cancelGpsTimelineAlignment = new TelemetryPlotContextMenuAction(
            "gps-cancel-alignment",
            "Cancel GPS alignment",
            cancelGpsTimelineAlignmentCommand);

        return CreateSignalPlotContextMenuActionsBySignalRowId(
            baseActions,
            setAnalysisRangeStart,
            setAnalysisRangeEnd,
            clearAnalysisRange,
            markGpsEvent,
            markGpsTelemetryEvent,
            cancelGpsTimelineAlignment);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreateSignalPlotContextMenuActionsBySignalRowId(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        TelemetryPlotContextMenuAction setAnalysisRangeStart,
        TelemetryPlotContextMenuAction setAnalysisRangeEnd,
        TelemetryPlotContextMenuAction clearAnalysisRange,
        TelemetryPlotContextMenuAction markGpsEvent,
        TelemetryPlotContextMenuAction markGpsTelemetryEvent,
        TelemetryPlotContextMenuAction cancelGpsTimelineAlignment)
    {
        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
        {
            [SignalRowIds.Travel] = AppendContextMenuActions(baseActions, SignalRowIds.Travel, setAnalysisRangeStart, setAnalysisRangeEnd, clearAnalysisRange, markGpsEvent, markGpsTelemetryEvent, cancelGpsTimelineAlignment),
            [SignalRowIds.Velocity] = AppendContextMenuActions(baseActions, SignalRowIds.Velocity, setAnalysisRangeStart, setAnalysisRangeEnd, clearAnalysisRange, markGpsEvent, markGpsTelemetryEvent, cancelGpsTimelineAlignment),
            [SignalRowIds.Imu] = AppendContextMenuActions(baseActions, SignalRowIds.Imu, setAnalysisRangeStart, setAnalysisRangeEnd, clearAnalysisRange, markGpsEvent, markGpsTelemetryEvent, cancelGpsTimelineAlignment),
            [SignalRowIds.PitchRoll] = AppendContextMenuActions(baseActions, SignalRowIds.PitchRoll, setAnalysisRangeStart, setAnalysisRangeEnd, clearAnalysisRange, markGpsEvent, markGpsTelemetryEvent, cancelGpsTimelineAlignment),
            [SignalRowIds.Speed] = AppendContextMenuActions(baseActions, SignalRowIds.Speed, setAnalysisRangeStart, setAnalysisRangeEnd, clearAnalysisRange, markGpsEvent, markGpsTelemetryEvent, cancelGpsTimelineAlignment),
            [SignalRowIds.Elevation] = AppendContextMenuActions(baseActions, SignalRowIds.Elevation, setAnalysisRangeStart, setAnalysisRangeEnd, clearAnalysisRange, markGpsEvent, markGpsTelemetryEvent, cancelGpsTimelineAlignment),
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
        var mapExpected = currentSnapshot?.FullTrackId is not null;
        if (currentSnapshot?.HasProcessedData == true)
        {
            presentationApplier.ClearRecordedPresentation();
            presentationApplier.ApplyRecordedLoadingStates(mapExpected);
            await PublishSessionLoadProgressAsync(
                SessionDetailLoadProgress.PreparingSession,
                mapExpected,
                currentSnapshot,
                token);
        }
        else
        {
            PublishLoadPresentation(new RecordedSessionLoadPresentation.Empty(currentSnapshot));
        }

        try
        {
            var dimensions = lastPresentationDimensions ?? SessionPresentationDimensions.Default;
            var progress = new SessionLoadProgressReporter(
                update => PublishSessionLoadProgress(update, mapExpected, currentSnapshot, token));
            var result = await sessionCoordinator.LoadDetailAsync(Id, dimensions, progress, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            await PublishSessionLoadProgressAsync(
                SessionDetailLoadProgress.ApplyingSessionData,
                mapExpected,
                sessionStore.Get(Id) ?? currentSnapshot,
                token);
            var loadPresentation = CreateLoadPresentation(result, sessionStore.Get(Id) ?? currentSnapshot);
            PublishLoadResultPresentation(loadPresentation);
            ApplyLoadedStateInputs(result);
            presentationApplier.ApplyLoadResult(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void PublishLoadResultPresentation(RecordedSessionLoadPresentation presentation)
    {
        if (presentation is not RecordedSessionLoadPresentation.Loaded)
        {
            PublishLoadPresentation(presentation);
            return;
        }

        suppressAnalysisRecompute = true;
        suppressInsightsRecompute = true;
        try
        {
            PublishLoadPresentation(presentation);
        }
        finally
        {
            suppressInsightsRecompute = false;
            suppressAnalysisRecompute = false;
        }
    }

    private void PublishLoadPresentation(RecordedSessionLoadPresentation presentation)
    {
        if (currentEditorState.Load != presentation)
        {
            PublishEditorInput(loadPresentationInput, presentation);
        }
    }

    private void PublishSessionLoadProgress(
        SessionDetailLoadProgress progress,
        bool mapExpected,
        SessionSnapshot? fallbackSnapshot,
        CancellationToken token)
    {
        if (token.IsCancellationRequested || !viewLoaded)
        {
            return;
        }

        if (UiThreadDispatcher.CheckAccess())
        {
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token);
            return;
        }

        UiThreadDispatcher.Post(() =>
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token));
    }

    private Task PublishSessionLoadProgressAsync(
        SessionDetailLoadProgress progress,
        bool mapExpected,
        SessionSnapshot? fallbackSnapshot,
        CancellationToken token)
    {
        if (token.IsCancellationRequested || !viewLoaded)
        {
            return Task.CompletedTask;
        }

        if (UiThreadDispatcher.CheckAccess())
        {
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token);
            return Task.CompletedTask;
        }

        return UiThreadDispatcher.InvokeAsync(() =>
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token));
    }

    private void PublishSessionLoadProgressOnCurrentThread(
        SessionDetailLoadProgress progress,
        bool mapExpected,
        SessionSnapshot? fallbackSnapshot,
        CancellationToken token)
    {
        if (token.IsCancellationRequested || !viewLoaded)
        {
            return;
        }

        PublishLoadPresentation(new RecordedSessionLoadPresentation.Loading(
            mapExpected,
            progress,
            sessionStore.Get(Id) ?? sessionSnapshot ?? fallbackSnapshot));
    }

    private sealed class SessionLoadProgressReporter : IProgress<SessionDetailLoadProgress>
    {
        private readonly Action<SessionDetailLoadProgress> onReport;

        public SessionLoadProgressReporter(Action<SessionDetailLoadProgress> onReport)
        {
            this.onReport = onReport;
        }

        public void Report(SessionDetailLoadProgress value)
        {
            onReport(value);
        }
    }

    private static RecordedSessionLoadPresentation CreateLoadPresentation(
        SessionDetailLoadResult result,
        SessionSnapshot? snapshot)
    {
        return result switch
        {
            SessionDetailLoadResult.Loaded loaded => new RecordedSessionLoadPresentation.Loaded(
                loaded.Data,
                snapshot),
            SessionDetailLoadResult.IncompleteLocalData incomplete =>
                new RecordedSessionLoadPresentation.IncompleteLocalData(
                    incomplete.Missing,
                    snapshot?.HasProcessedData ?? false,
                    snapshot),
            SessionDetailLoadResult.Failed failed => new RecordedSessionLoadPresentation.Failed(
                failed.ErrorMessage,
                snapshot),
            _ => new RecordedSessionLoadPresentation.Empty(snapshot),
        };
    }

    private void ApplyLoadedStateInputs(SessionDetailLoadResult result)
    {
        if (result is not SessionDetailLoadResult.Loaded loaded)
        {
            return;
        }

        var telemetryPresentation = loaded.Data.TelemetryPresentation;
        ApplyDampingSpeedCutoffContext(
            telemetryPresentation.DampingSpeedCutoffs,
            telemetryPresentation.DampingSpeedCutoffOwner);
        RequestCurrentDampingPercentages();
    }

    private async Task ApplyPersistedSnapshotAsync(SessionSnapshot snapshot)
    {
        session = snapshot.ToMetadataEntity();
        sessionSnapshot = snapshot;
        BaselineUpdated = snapshot.Updated;
        metadataConflictPending = false;
        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
    }

    // Single entry point for projection emissions on the opened session. It treats the
    // derived and metadata axes orthogonally: derived telemetry always refreshes
    // (even while a metadata prompt is pending), and the metadata prompt is decided
    // independently so unsaved edits are never silently discarded.
    private async Task OnDomainChangedAsync(RecordedSessionDomainSnapshot domain)
    {
        // The domain-change effect is fire-and-forget; an unguarded throw would
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
        sessionSnapshot = snapshot;
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

    private RecordedSessionHostRuntimeState CreateRecordedSessionHostRuntimeState()
    {
        return new RecordedSessionHostRuntimeState(
            Id,
            viewLoaded,
            IsTabActive,
            pendingTimelineAlignmentMark);
    }

    private void PublishRecordedSessionHostRuntimeChange()
    {
        PublishEditorInput(hostRuntimeInput, CreateRecordedSessionHostRuntimeState());
    }

    private void PublishEditorInput<T>(Subject<T> input, T value)
    {
        if (!editorInputSubjectsDisposed)
        {
            input.OnNext(value);
        }
    }

    private void DisposeEditorInputSubjects()
    {
        if (editorInputSubjectsDisposed)
        {
            return;
        }

        editorInputSubjectsDisposed = true;
        editorInputSubjects.Dispose();
    }

    private async ValueTask InitializeRecordedSessionExtensionsAsync(CancellationToken cancellationToken = default)
    {
        if (recordedSessionExtensions is null)
        {
            return;
        }

        await recordedSessionExtensions.InitializeAsync(
            RecordedSessionHostStateProjection.Create(
                currentEditorState,
                CreateRecordedSessionHostRuntimeState(),
                Timeline),
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

    private async ValueTask DisposeRecordedSessionExtensionsAsync()
    {
        if (recordedSessionExtensions is null)
        {
            return;
        }

        await recordedSessionExtensions.DisposeAsync();
    }

    private void DisposeProcessedTelemetryRetention()
    {
        processedTelemetryRetention?.Dispose();
        processedTelemetryRetention = null;
    }

    private void ReportRecordedSessionExtensionOperation(string message, double percent)
    {
        SetSessionOperationState(SessionOperationPresentationState.Progress(message, percent));
    }

    private void CompleteRecordedSessionExtensionOperation()
    {
        SetSessionOperationState(SessionOperationPresentationState.Hidden);
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
        cancelGpsTimelineAlignmentCommand.NotifyCanExecuteChanged();
    }

    private static bool IsTelemetryPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return context is { ClickSeconds: >= 0 } &&
               double.IsFinite(context.ClickSeconds) &&
               context.RowId is SignalRowIds.Travel or
                   SignalRowIds.Velocity or
                   SignalRowIds.Imu or
                   SignalRowIds.PitchRoll or
                   SignalRowIds.Speed or
                   SignalRowIds.Elevation;
    }

    private bool CanMarkGpsEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is null &&
               telemetryData is not null &&
               trackPoints is { Count: > 0 } &&
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

    private bool CanSetAnalysisRangeFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return telemetryData is not null &&
               IsTelemetryPlotContext(context);
    }

    private bool CanClearAnalysisRangeFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return (currentEditorState.Intent.AnalysisRange is not null ||
                currentEditorState.Intent.PendingAnalysisRangeBoundary is not null) &&
               IsTelemetryPlotContext(context);
    }

    private void SetAnalysisRangeStartFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanSetAnalysisRangeFromPlotContext(context))
        {
            return;
        }

        editorActions.SetAnalysisRangeStartBoundary(context!.ClickSeconds);
    }

    private void SetAnalysisRangeEndFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanSetAnalysisRangeFromPlotContext(context))
        {
            return;
        }

        editorActions.SetAnalysisRangeEndBoundary(context!.ClickSeconds);
    }

    private void ClearAnalysisRangeFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanClearAnalysisRangeFromPlotContext(context))
        {
            return;
        }

        ClearAnalysisRange();
    }

    private bool CanMarkGpsTelemetryEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return telemetryData is not null &&
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
            telemetryData is not { } telemetry)
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

    private bool CanCancelGpsTimelineAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return IsPendingTimelineAlignment(RecordedSessionTimelineAlignmentTarget.GpsTrack, subjectId: null) &&
               IsTelemetryPlotContext(context);
    }

    private void CancelGpsTimelineAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanCancelGpsTimelineAlignmentFromPlotContext(context))
        {
            return;
        }

        _ = ((IRecordedSessionHostOperations)this).TryCancelTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.GpsTrack,
            subjectId: null);
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
        PublishRecordedSessionHostRuntimeChange();
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
        PublishRecordedSessionHostRuntimeChange();
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
        PublishRecordedSessionHostRuntimeChange();
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

    Task<Guid?> IRecordedSessionHostOperations.CreateDerivedSessionAsync(
        Guid fromSessionId,
        string name,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken) =>
        sessionCoordinator.CreateDerivedSessionAsync(fromSessionId, name, sourceAbsoluteStartSeconds, cancellationToken);

    async Task<bool> IRecordedSessionHostOperations.DeleteSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await sessionCoordinator.DeleteAsync(sessionId);
        return result.Outcome == SessionDeleteOutcome.Deleted;
    }

    Task<bool> IRecordedSessionHostOperations.UpdateSessionOriginAsync(
        Guid sessionId,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken) =>
        sessionCoordinator.UpdateSessionOriginAsync(sessionId, sourceAbsoluteStartSeconds, cancellationToken);

    Task<bool> IRecordedSessionHostOperations.RenameSessionAsync(
        Guid sessionId,
        string name,
        CancellationToken cancellationToken) =>
        sessionCoordinator.RenameSessionAsync(sessionId, name, cancellationToken);

    async Task<bool> IRecordedSessionHostOperations.RequestRecomputeAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await recordedSessionDerivationWindowCache.RefreshSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await sessionCoordinator.RequestRecomputeAsync(sessionId, RecomputeReason.SourceWindowChanged);
        return result is SessionRecomputeResult.Recomputed or SessionRecomputeResult.Superseded;
    }

    async Task IRecordedSessionHostOperations.OpenSessionInBackgroundAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null)
        {
            return;
        }

        await UiThreadDispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            editorFactory().OpenSessionDetailInBackground(snapshot);
        });
    }

    Guid ISessionOperationGateway.SessionId => Id;

    bool ISessionOperationGateway.IsDirty => IsDirty;

    bool ISessionOperationGateway.IsViewLoaded => viewLoaded;

    bool ISessionOperationGateway.ShouldDeferDomainHandling() => ShouldDeferDomainHandling();

    void ISessionOperationGateway.SetSignalLayoutPreferences(SignalLayoutPreferences preferences) =>
        SignalLayoutPreferences = preferences;

    void ISessionOperationGateway.RequestSessionInsights() => RecomputeSessionInsights();

    #endregion

    #region Constructors

    internal SessionDetailViewModel(
        SessionSnapshot snapshot,
        ISessionCoordinator sessionCoordinator,
        ITrackCoordinator trackCoordinator,
        ISessionStore sessionStore,
        IRecordedSessionProjection recordedSessionProjection,
        IMapViewModelFactory mapViewModelFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISessionPreferences sessionPreferences,
        IUiThreadDispatcher uiThreadDispatcher,
        bool deferDomainHandlingWhenInactive,
        IRecordedSessionProcessingOptionCache recordedSessionProcessingOptionCache,
        ISessionProcessedTelemetryReader processedTelemetryReader,
        IRecordedSessionAnalysisResultStateFactory analysisResultStateFactory,
        IRecordedSessionDerivationWindowCache recordedSessionDerivationWindowCache,
        Func<IEditorFactory> editorFactory,
        IBikeCoordinator? bikeCoordinator = null,
        ExtensionHostDependencies? extensionHost = null,
        ILayoutProfileTransitionState? layoutProfileTransitionState = null)
        : base(shell, dialogService, uiThreadDispatcher)
    {
        ArgumentNullException.ThrowIfNull(sessionPreferences);
        this.deferDomainHandlingWhenInactive = deferDomainHandlingWhenInactive;

        this.sessionCoordinator = sessionCoordinator;
        this.trackCoordinator = trackCoordinator;
        this.bikeCoordinator = bikeCoordinator;
        this.sessionStore = sessionStore;
        this.recordedSessionProjection = recordedSessionProjection;
        this.processedTelemetryReader = processedTelemetryReader;
        this.recordedSessionProcessingOptionCache = recordedSessionProcessingOptionCache;
        analysisResultState = analysisResultStateFactory.Create(() => telemetryData);
        analysisInputs = CreateCurrentAnalysisInputs();
        analysisResultState.Invalidate(analysisInputs);
        analysisRequestScheduler = new AnalysisRequestScheduler(this, analysisInputs);
        analysisResultSubscription = analysisResultState.Connect().Subscribe(OnAnalysisResultChanged);
        editorInputSubjects = new CompositeDisposable(
            pageCountInput,
            preferenceReplayInput,
            loadPresentationInput,
            sessionOperationStateInput,
            mediaPaneStateInput,
            mediaUrlInput,
            dampingPercentagesInput,
            plotDampingSpeedCutoffsInput,
            canEditDampingSpeedCutoffsInput,
            sessionInsightsInput,
            analysisSelectionInput,
            signalPlotContextMenuActionsInput,
            domainInput,
            hostRuntimeInput,
            dirtyBaselineInput,
            stalenessReplayInput);
        editorStateController = new RecordedSessionEditorStateController(
            new RecordedSessionEditorStateInputs(
                editorActions.Intents,
                pageCountInput,
                preferenceReplayInput,
                loadPresentationInput,
                sessionOperationStateInput,
                mediaPaneStateInput,
                mediaUrlInput,
                dampingPercentagesInput,
                plotDampingSpeedCutoffsInput,
                canEditDampingSpeedCutoffsInput,
                sessionInsightsInput,
                analysisSelectionInput,
                signalPlotContextMenuActionsInput,
                domainInput));
        editorStateSubscription = editorStateController.State.Subscribe(ApplyEditorState);
        this.recordedSessionDerivationWindowCache = recordedSessionDerivationWindowCache;
        this.editorFactory = editorFactory;
        this.layoutProfileTransitionState = layoutProfileTransitionState ?? new LayoutProfileTransitionState();
        recordedPreferenceStore = new RecordedPreferenceStore(
            sessionPreferences,
            () => Id,
            ErrorMessages.Add);
        signalRowActions = new SignalRowActionsController(
            () => currentEditorState.AnalysisSelection.HighlightRanges.Count > 0,
            () => currentEditorState.Presentation.Signals.ShowAirtime,
            SetShowAirtime,
            () => currentEditorState.Presentation.Signals.ShowVelocityAirtime,
            SetShowVelocityAirtime,
            () => currentEditorState.Presentation.Signals.ShowImuAirtime,
            SetShowImuAirtime,
            () => currentEditorState.Presentation.Signals.ShowPitchRollAirtime,
            SetShowPitchRollAirtime,
            () => currentEditorState.Presentation.Signals.ShowSpeedAirtime,
            SetShowSpeedAirtime,
            () => currentEditorState.Presentation.Signals.ShowElevationAirtime,
            SetShowElevationAirtime,
            () => currentEditorState.Presentation.Signals.ShowAnalysisSelection,
            SetShowAnalysisSelection,
            () => currentEditorState.Presentation.Signals.ShowVelocityAnalysisSelection,
            SetShowVelocityAnalysisSelection,
            () => currentEditorState.Presentation.Signals.ShowImuAnalysisSelection,
            SetShowImuAnalysisSelection,
            () => currentEditorState.Presentation.Signals.ShowPitchRollAnalysisSelection,
            SetShowPitchRollAnalysisSelection,
            () => currentEditorState.Presentation.Signals.ShowSpeedAnalysisSelection,
            SetShowSpeedAnalysisSelection,
            () => currentEditorState.Presentation.Signals.ShowElevationAnalysisSelection,
            SetShowElevationAnalysisSelection);
        signalAutozoomController = new SignalAutozoomController(Timeline);
        setAnalysisRangeStartCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            SetAnalysisRangeStartFromPlotContext,
            CanSetAnalysisRangeFromPlotContext);
        setAnalysisRangeEndCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            SetAnalysisRangeEndFromPlotContext,
            CanSetAnalysisRangeFromPlotContext);
        clearAnalysisRangeFromContextCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            ClearAnalysisRangeFromPlotContext,
            CanClearAnalysisRangeFromPlotContext);
        markGpsEventCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            MarkGpsEventFromPlotContext,
            CanMarkGpsEventFromPlotContext);
        markGpsTelemetryEventCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            MarkGpsTelemetryEventFromPlotContextAsync,
            CanMarkGpsTelemetryEventFromPlotContext);
        cancelGpsTimelineAlignmentCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            CancelGpsTimelineAlignmentFromPlotContext,
            CanCancelGpsTimelineAlignmentFromPlotContext);
        SignalPlotContextMenuActionsBySignalRowId = CreateSignalPlotContextMenuActionsBySignalRowId(
            signalAutozoomController.ActionsBySignalRowId,
            setAnalysisRangeStartCommand,
            setAnalysisRangeEndCommand,
            clearAnalysisRangeFromContextCommand,
            markGpsEventCommand,
            markGpsTelemetryEventCommand,
            cancelGpsTimelineAlignmentCommand);
        PublishEditorInput(signalPlotContextMenuActionsInput, SignalPlotContextMenuActionsBySignalRowId);
        session = snapshot.ToMetadataEntity();
        sessionSnapshot = snapshot;
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        MobileWorkspace = new SessionShellMobileWorkspaceViewModel(
            this,
            Pages,
            editorStateController.State,
            editorActions);
        SignalsWorkspace = new RecordedSessionSignalsWorkspaceViewModel(
            editorStateController.State,
            SourceVisibility,
            Timeline,
            () => ExtensionSlots,
            editorActions);
        MediaWorkspace = new SessionMediaWorkspaceViewModel(
            editorStateController.State,
            () => MapViewModel,
            Timeline,
            () => ExtensionSlots);
        AnalysisWorkspace = new SessionAnalysisWorkspaceViewModel(
            editorStateController.State,
            () => ExtensionSlots,
            this,
            editorActions,
            SelectAnalysisRangeCommand,
            analysisResultState);
        SidebarWorkspace = new SessionSidebarWorkspaceViewModel(
            this,
            () => Name,
            value => Name = value,
            NotesPage,
            PreferencesPage,
            SaveCommand,
            ResetCommand);
        dampingCutoffWorkflow = new DampingCutoffWorkflow(
            () => currentEditorState.Intent.DampingSpeedCutoffs,
            SetCanEditDampingSpeedCutoffs,
            editorActions.SetDampingSpeedCutoffs,
            SetPlotDampingSpeedCutoffs,
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
            extensionPagesController = new RecordedSessionExtensionPagesController(recordedSessionExtensions, Pages, editorActions);
        }

        SignalsPage = new RecordedSignalsPageViewModel(SignalsWorkspace, MediaWorkspace);
        SpringPage = new SpringPageViewModel(AnalysisWorkspace);
        StrokesPage = new StrokesPageViewModel(AnalysisWorkspace);
        DampingPage = new DampingPageViewModel(AnalysisWorkspace);
        BalancePage = new BalancePageViewModel(AnalysisWorkspace);
        VibrationPage = new VibrationPageViewModel(AnalysisWorkspace);
        AnalysisPage = new SessionInsightsPageViewModel(AnalysisWorkspace);
        presentationApplier = new RecordedPagePresentationApplier(
            Pages,
            SpringPage,
            DampingPage,
            BalancePage,
            VibrationPage,
            AnalysisPage,
            NotesPage);
        Pages.Add(SignalsPage);
        Pages.Add(SpringPage);
        Pages.Add(StrokesPage);
        Pages.Add(DampingPage);
        Pages.Add(BalancePage);
        Pages.Add(VibrationPage);
        Pages.Add(AnalysisPage);
        Pages.Add(NotesPage);
        Pages.Add(PreferencesPage);
        Pages.CollectionChanged += OnPagesChanged;
        PublishEditorInput(pageCountInput, Pages.Count);
        mapViewModel = mapViewModelFactory.Create();
        if (snapshot.HasProcessedData)
        {
            presentationApplier.ApplyRecordedLoadingStates(snapshot.FullTrackId is not null);
            PublishLoadPresentation(new RecordedSessionLoadPresentation.Loading(
                snapshot.FullTrackId is not null,
                SessionDetailLoadProgress.PreparingSession,
                snapshot));
        }

        NotesPage.ForkSettings.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        NotesPage.ShockSettings.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        NotesPage.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        SubscribeSignalPreferenceChanges();
        PreferencesPage.ProcessingPreferenceChangeCommitted += OnProcessingPreferenceChangeCommitted;

        ResetImplementation();
        SetSignalPresentationState(CreateSignalPresentationState());
        if (!snapshot.HasProcessedData)
        {
            PublishCurrentLoadPresentation();
        }

        editorEffects = new RecordedSessionEditorEffects(
            [
                RecordedSessionEditorEffects.PreferencePersistence(editorActions.Intents),
                RecordedSessionEditorEffects.AnalysisRequests(editorStateController.State),
                RecordedSessionEditorEffects.PageSelectionAnalysisRequests(editorStateController.State),
                RecordedSessionEditorEffects.TelemetryAnalysisRequests(editorStateController.State),
                RecordedSessionEditorEffects.ExplicitAnalysisRequests(editorActions.Intents),
                RecordedSessionEditorEffects.MapMediaSync(editorStateController.State),
                RecordedSessionEditorEffects.CommandRefresh(editorStateController.State),
                RecordedSessionEditorEffects.RecomputeStaleness(
                    editorStateController.State,
                    stalenessReplayInput),
                RecordedSessionEditorEffects.DirtyBaselineTracking(dirtyBaselineInput),
                RecordedSessionEditorEffects.ExtensionHostPublication(
                    editorStateController.State,
                    hostRuntimeInput.StartWith(CreateRecordedSessionHostRuntimeState()),
                    Timeline),
            ],
            ApplyRecordedSessionEditorEffect);
    }

    #endregion

    #region Private methods

    private void ApplyEditorState(RecordedSessionEditorState state)
    {
        var previous = currentEditorState;
        currentEditorState = state;
        if (state.Presentation.MapState.ReservesLayout && !mapInitializeRequested)
        {
            mapInitializeRequested = true;
            _ = mapViewModel?.InitializeAsync();
        }

        var isComplete = IsCompleteFromLoadPresentation(state.Load);
        if (IsComplete != isComplete)
        {
            IsComplete = isComplete;
        }

        if (previous.Session != state.Session)
        {
            sessionSnapshot = state.Session;
            if (state.Session is { } snapshot)
            {
                ApplyRuntimeSessionState(snapshot);
            }

            OnPropertyChanged(nameof(CurrentSessionSnapshot));
        }

        if (previous.TelemetryData != state.TelemetryData)
        {
            if (telemetryData != state.TelemetryData)
            {
                telemetryData = state.TelemetryData;
                telemetryGeneration++;
            }

            PreferencesPage.SampleRate = state.TelemetryData?.Metadata.SampleRate ?? 0;
            NotesPage.SetTemperatureAverages(state.TelemetryData?.TemperatureAverages ?? []);
            OnPropertyChanged(nameof(CurrentTelemetryData));
        }

        if (previous.FullTrackPoints != state.FullTrackPoints)
        {
            fullTrackPoints = ToTrackPointList(state.FullTrackPoints);
        }

        if (previous.TrackPoints != state.TrackPoints)
        {
            trackPoints = ToTrackPointList(state.TrackPoints);
            OnPropertyChanged(nameof(CurrentTrackPoints));
        }

        if (previous.Intent.SelectedPageIndex != state.Intent.SelectedPageIndex)
        {
            OnPropertyChanged(nameof(SelectedPage));
        }

        if (previous.Intent.AnalysisRange != state.Intent.AnalysisRange)
        {
            OnPropertyChanged(nameof(CurrentAnalysisRange));
            OnPropertyChanged(nameof(SessionAnalysisRangeText));
        }
        if (previous.AnalysisSelection != state.AnalysisSelection)
        {
            OnPropertyChanged(nameof(ActiveFrontAnalysisSelection));
            OnPropertyChanged(nameof(ActiveRearAnalysisSelection));
            signalRowActions.RefreshAnalysisSelectionActionStates();
        }

        if (previous.Presentation.Signals != state.Presentation.Signals)
        {
            signalRowActions.RefreshAirtimeActionStates();
            signalRowActions.RefreshAnalysisSelectionActionStates();
        }

        if (previous.Presentation.SignalAvailability != state.Presentation.SignalAvailability)
        {
            ApplySignalAvailability(state.Presentation.SignalAvailability);
        }

        if (previous.Presentation.ScreenState != state.Presentation.ScreenState)
        {
            OnPropertyChanged(nameof(ScreenState));
        }

        if (previous.Presentation.OperationState != state.Presentation.OperationState)
        {
            OnPropertyChanged(nameof(SessionOperationState));
        }

        if (previous.Presentation.CanEditDampingSpeedCutoffs != state.Presentation.CanEditDampingSpeedCutoffs)
        {
            OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
        }

        if (previous.Intent.SignalDisplayPreferences != state.Intent.SignalDisplayPreferences)
        {
            OnPropertyChanged(nameof(SignalDisplayPreferences));
        }

        if (previous.Intent.SignalLayoutPreferences != state.Intent.SignalLayoutPreferences)
        {
            OnPropertyChanged(nameof(SignalLayoutPreferences));
        }

        if (previous.Intent.LayoutPreferences != state.Intent.LayoutPreferences)
        {
            OnPropertyChanged(nameof(LayoutPreferences));
            OnPropertyChanged(nameof(MediaLayoutPreferences));
        }

        var travelDistributionChanged =
            previous.Intent.SelectedTravelDistributionMode != state.Intent.SelectedTravelDistributionMode;
        var velocityAverageChanged =
            previous.Intent.SelectedVelocityAverageMode != state.Intent.SelectedVelocityAverageMode;
        var balanceDisplacementChanged =
            previous.Intent.SelectedBalanceDisplacementMode != state.Intent.SelectedBalanceDisplacementMode;
        var balanceSpeedChanged =
            previous.Intent.SelectedBalanceSpeedMode != state.Intent.SelectedBalanceSpeedMode;

        if (travelDistributionChanged ||
            velocityAverageChanged ||
            balanceDisplacementChanged ||
            balanceSpeedChanged)
        {
            OnPropertyChanged(nameof(SessionAnalysisModesText));
        }
    }

    private static bool IsCompleteFromLoadPresentation(RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Loaded => true,
            RecordedSessionLoadPresentation.IncompleteLocalData incomplete => incomplete.HasProcessedData,
            RecordedSessionLoadPresentation.Loading loading => loading.Session?.HasProcessedData ?? false,
            RecordedSessionLoadPresentation.Empty empty => empty.Session?.HasProcessedData ?? false,
            RecordedSessionLoadPresentation.Failed failed => failed.Session?.HasProcessedData ?? false,
            _ => false,
        };
    }

    private void ApplyRuntimeSessionState(SessionSnapshot snapshot)
    {
        session.FullTrack = snapshot.FullTrackId;
        session.GpsOffsetSeconds = snapshot.GpsOffsetSeconds;
        session.Updated = snapshot.Updated;
    }

    private void ClearAnalysisSelections()
    {
        PublishEditorInput(analysisSelectionInput, CreateEmptyAnalysisSelectionState());
        signalRowActions.ClearAnalysisSelectionToggles();
        signalRowActions.RefreshAnalysisSelectionActionStates();
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        PublishEditorInput(pageCountInput, Pages.Count);
        editorActions.RefreshSelectedPageAnalysis();
    }

    private void PublishCurrentLoadPresentation()
    {
        PublishLoadPresentation(CreateCurrentLoadPresentation());
    }

    private RecordedSessionLoadPresentation CreateCurrentLoadPresentation()
    {
        var snapshot = sessionSnapshot;
        return currentEditorState.Load switch
        {
            RecordedSessionLoadPresentation.Loaded loaded when telemetryData is not null =>
                loaded with
                {
                    Data = loaded.Data with
                    {
                        TelemetryPresentation = CreateCurrentTelemetryPresentation(
                            loaded.Data.TelemetryPresentation)
                    },
                    Session = snapshot,
                },
            RecordedSessionLoadPresentation.Loading loading => loading with
            {
                Session = snapshot,
            },
            RecordedSessionLoadPresentation.IncompleteLocalData incomplete => incomplete with
            {
                HasProcessedData = snapshot?.HasProcessedData ?? incomplete.HasProcessedData,
                Session = snapshot,
            },
            RecordedSessionLoadPresentation.Failed failed => failed with
            {
                Session = snapshot,
            },
            _ => new RecordedSessionLoadPresentation.Empty(snapshot),
        };
    }

    private SessionTelemetryPresentationData CreateCurrentTelemetryPresentation(
        SessionTelemetryPresentationData current)
    {
        return current with
        {
            TelemetryData = telemetryData!,
            FullTrackId = sessionSnapshot?.FullTrackId ?? session.FullTrack,
            FullTrackPoints = fullTrackPoints,
            TrackPoints = trackPoints,
            MediaColumnWidth = currentEditorState.Presentation.MediaColumnWidth,
        };
    }

    private static AnalysisSelectionState CreateEmptyAnalysisSelectionState()
    {
        return new AnalysisSelectionState(
            ActiveFront: null,
            ActiveRear: null,
            HighlightRanges: []);
    }

    private RecordedSignalPresentationState CreateSignalPresentationState()
    {
        var state = currentEditorState.Presentation.Signals;
        return state with
        {
            ShowAirtime = HasSignalHeaderActions(state) ? state.ShowAirtime : true,
            TravelHeaderActions = TravelHeaderActions,
            VelocityHeaderActions = VelocityHeaderActions,
            ImuHeaderActions = ImuHeaderActions,
            PitchRollHeaderActions = PitchRollHeaderActions,
            SpeedHeaderActions = SpeedHeaderActions,
            ElevationHeaderActions = ElevationHeaderActions,
        };
    }

    private static bool HasSignalHeaderActions(RecordedSignalPresentationState state)
    {
        return state.TravelHeaderActions.Count > 0 ||
               state.VelocityHeaderActions.Count > 0 ||
               state.ImuHeaderActions.Count > 0 ||
               state.PitchRollHeaderActions.Count > 0 ||
               state.SpeedHeaderActions.Count > 0 ||
               state.ElevationHeaderActions.Count > 0;
    }

    private void SetShowAirtime(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowAirtime = value });

    private void SetShowVelocityAirtime(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowVelocityAirtime = value });

    private void SetShowImuAirtime(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowImuAirtime = value });

    private void SetShowPitchRollAirtime(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowPitchRollAirtime = value });

    private void SetShowSpeedAirtime(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowSpeedAirtime = value });

    private void SetShowElevationAirtime(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowElevationAirtime = value });

    private void SetShowAnalysisSelection(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowAnalysisSelection = value });

    private void SetShowVelocityAnalysisSelection(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowVelocityAnalysisSelection = value });

    private void SetShowImuAnalysisSelection(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowImuAnalysisSelection = value });

    private void SetShowPitchRollAnalysisSelection(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowPitchRollAnalysisSelection = value });

    private void SetShowSpeedAnalysisSelection(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowSpeedAnalysisSelection = value });

    private void SetShowElevationAnalysisSelection(bool value) =>
        SetSignalPresentationState(CreateSignalPresentationState() with { ShowElevationAnalysisSelection = value });

    private void ApplySignalAvailability(RecordedSignalAvailabilityState availability)
    {
        PreferencesPage.ApplySignalAvailability(
            availability.Travel,
            availability.Velocity,
            availability.Imu,
            availability.PitchRoll,
            availability.Speed,
            availability.Elevation);
    }

    private void SetSignalPresentationState(RecordedSignalPresentationState state)
    {
        editorActions.SetSignalPresentation(state);
    }

    private void SetCanEditDampingSpeedCutoffs(bool value)
    {
        PublishEditorInput(canEditDampingSpeedCutoffsInput, value);
    }

    private void SetPlotDampingSpeedCutoffs(DampingSpeedCutoffs cutoffs)
    {
        PublishEditorInput(plotDampingSpeedCutoffsInput, cutoffs);
    }

    internal void SetSessionOperationState(SessionOperationPresentationState state)
    {
        PublishEditorInput(sessionOperationStateInput, state);
    }

    internal void SetSessionInsights(SessionInsightsResult insights)
    {
        PublishEditorInput(sessionInsightsInput, insights);
    }

    internal void SetMediaUrl(string? url)
    {
        var nextPaneState = string.IsNullOrWhiteSpace(url)
            ? SurfacePresentationState.Hidden
            : SurfacePresentationState.Ready;
        PublishEditorInput(mediaUrlInput, url);
        PublishEditorInput(mediaPaneStateInput, nextPaneState);
    }

    private void EvaluateDirtinessFromPageChange()
    {
        if (suppressDirtinessEvaluation)
        {
            return;
        }

        PublishEditorInput(dirtyBaselineInput, Unit.Default);
    }

    private void OnNotesPageDirtinessPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        EvaluateDirtinessFromPageChange();
    }

    private async Task RestoreRecordedPreferencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await recordedPreferenceStore.RestoreAsync(preferences =>
        {
            if (!viewLoaded || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ApplyRecordedPreferences(preferences);
        });
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void ApplyRecordedPreferences(SessionPreferences preferences)
    {
        analysisRequestScheduler.BeginBatch(suppressInsights: true);
        suppressInsightsRecompute = true;
        try
        {
            ApplyPreferencesPageSignalDisplayPreferences(preferences.SignalDisplay);
            PreferencesPage.ApplyProcessingPreferences(preferences.Processing);
            PublishEditorInput(preferenceReplayInput, preferences);
        }
        finally
        {
            suppressInsightsRecompute = false;
            analysisRequestScheduler.EndBatch();
        }
    }

    private void ApplyPreferencesPageSignalDisplayPreferences(SignalDisplayPreferences preferences)
    {
        UnsubscribeSignalPreferenceChanges();
        try
        {
            PreferencesPage.ApplySignalDisplayPreferences(preferences);
        }
        finally
        {
            SubscribeSignalPreferenceChanges();
        }
    }

    private void SubscribeSignalPreferenceChanges()
    {
        PreferencesPage.TravelSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.VelocitySignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.ImuSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.PitchRollSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.SpeedSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.ElevationSignal.PropertyChanged += OnSignalPreferenceChanged;
    }

    private void UnsubscribeSignalPreferenceChanges()
    {
        PreferencesPage.TravelSignal.PropertyChanged -= OnSignalPreferenceChanged;
        PreferencesPage.VelocitySignal.PropertyChanged -= OnSignalPreferenceChanged;
        PreferencesPage.ImuSignal.PropertyChanged -= OnSignalPreferenceChanged;
        PreferencesPage.PitchRollSignal.PropertyChanged -= OnSignalPreferenceChanged;
        PreferencesPage.SpeedSignal.PropertyChanged -= OnSignalPreferenceChanged;
        PreferencesPage.ElevationSignal.PropertyChanged -= OnSignalPreferenceChanged;
    }

    private void OnSignalPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not nameof(SignalPreferenceItemViewModel.SelectedSmoothing))
        {
            return;
        }

        var signalDisplay = PreferencesPage.CreateSignalDisplayPreferences();
        editorActions.SetSignalDisplayPreferences(signalDisplay);
    }

    private void ApplyRecordedSessionEditorEffect(RecordedSessionEditorEffect effect)
    {
        if (effect is RecordedSessionEditorEffect.PersistPreferences persist)
        {
            PersistRecordedPreferenceIntent(persist.Intent);
            return;
        }

        if (effect is RecordedSessionEditorEffect.RequestAnalysis request)
        {
            ApplyRecordedAnalysisRequest(request.Request);
            return;
        }

        if (effect is RecordedSessionEditorEffect.SyncMapMedia sync)
        {
            ApplyMapMediaSync(sync.LoadedData);
            return;
        }

        if (effect is RecordedSessionEditorEffect.RefreshCommands)
        {
            NotifyTimelineAlignmentCommandsCanExecuteChanged();
            return;
        }

        if (effect is RecordedSessionEditorEffect.EvaluateRecomputeStaleness staleness)
        {
            _ = OnDomainChangedAsync(staleness.Domain);
            return;
        }

        if (effect is RecordedSessionEditorEffect.ReplayRecomputeStaleness stalenessReplay)
        {
            _ = ReplayStalenessOnLoadAsync(stalenessReplay.Domain);
            return;
        }

        if (effect is RecordedSessionEditorEffect.UpdateDirtyBaseline)
        {
            EvaluateDirtiness();
            return;
        }

        if (effect is RecordedSessionEditorEffect.PublishExtensionHostState publish)
        {
            recordedSessionExtensions?.UpdateHostState(publish.State);
        }
    }

    private async Task ReplayStalenessOnLoadAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!viewLoaded)
        {
            return;
        }

        await stalenessReconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);
    }

    private void ApplyMapMediaSync(RecordedSessionLoadedData loadedData)
    {
        if (MapViewModel is not { } map)
        {
            return;
        }

        map.FullTrackPoints = ToTrackPointList(loadedData.FullTrackPoints);
        map.SessionTrackPoints = ToTrackPointList(loadedData.TrackPoints);
        map.TimelineContext = loadedData.TrackTimelineContext;
    }

    private static List<TrackPoint>? ToTrackPointList(IReadOnlyList<TrackPoint>? points)
    {
        return points switch
        {
            null => null,
            List<TrackPoint> list => list,
            _ => [.. points],
        };
    }

    private void ApplyRecordedAnalysisRequest(RecordedSessionAnalysisEffectRequest request)
    {
        switch (request)
        {
            case RecordedSessionAnalysisEffectRequest.RangeChanged:
                if (suppressAnalysisRecompute)
                {
                    InvalidateAnalysisInputs();
                }
                else
                {
                    RequestCurrentAnalysisResults(!suppressInsightsRecompute, respectSuppression: true);
                }

                break;

            case RecordedSessionAnalysisEffectRequest.SelectedPageChanged:
                if (IsSessionInsightsPageSelected)
                {
                    RequestCurrentSessionInsights(respectSuppression: true);
                }

                break;

            case RecordedSessionAnalysisEffectRequest.TelemetryChanged:
                ApplyTelemetryAnalysisChange();
                break;

            case RecordedSessionAnalysisEffectRequest.Damping damping:
                RequestCurrentAnalysisResults(damping.IncludeInsights, damping.RespectSuppression);
                break;

            case RecordedSessionAnalysisEffectRequest.Insights insights:
                RequestCurrentSessionInsights(insights.RespectSuppression);
                break;
        }
    }

    private void ApplyTelemetryAnalysisChange()
    {
        if (telemetryData is null)
        {
            analysisRequestScheduler.OnTelemetryUnavailable();
            return;
        }

        var includeDeferredInsights =
            analysisRequestScheduler.ConsumePendingTelemetryInsightsRequest() ||
            IsSessionInsightsPageSelected;

        if (currentEditorState.Intent.AnalysisRange is not null)
        {
            ClearAnalysisRange();
            if (suppressAnalysisRecompute && includeDeferredInsights)
            {
                RequestCurrentAnalysisResults(includeInsights: true);
            }

            return;
        }

        if (suppressAnalysisRecompute)
        {
            InvalidateAnalysisInputs();
            if (includeDeferredInsights)
            {
                RequestCurrentAnalysisResults(includeInsights: true);
            }
        }
        else
        {
            RequestCurrentAnalysisResults(!suppressInsightsRecompute || includeDeferredInsights);
        }
    }

    private void PersistRecordedPreferenceIntent(RecordedSessionEditorIntent intent)
    {
        var update = RecordedSessionEditorIntentReducers.CreatePreferenceUpdate(intent);
        if (update is null)
        {
            return;
        }

        var current = recordedPreferenceStore.Current;
        var next = update(current);
        if (next == current)
        {
            return;
        }

        recordedPreferenceStore.UpdateCurrent(_ => next);
        _ = recordedPreferenceStore.PersistChangeAsync(update);
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
                    session = conflict.CurrentSnapshot.ToMetadataEntity();
                    sessionSnapshot = conflict.CurrentSnapshot;
                    BaselineUpdated = conflict.CurrentSnapshot.Updated;
                    metadataConflictPending = false;
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
        UnsubscribeVmEventHandlers();
        DisposeWorkspace(MobileWorkspace);
        DisposeWorkspace(SignalsWorkspace);
        DisposeWorkspace(MediaWorkspace);
        DisposeWorkspace(AnalysisWorkspace);
        extensionPagesController?.Dispose();
        editorEffects.Dispose();
        editorStateSubscription.Dispose();
        editorStateController.Dispose();
        editorActions.Dispose();
        await DisposeRecordedSessionExtensionsAsync();
        viewLoadOperation.Dispose();
        loadOperation.Dispose();
        analysisResultSubscription.Dispose();
        analysisResultState.Dispose();
        MapViewModel?.Dispose();
        DisposeEditorInputSubjects();
    }

    private static void DisposeWorkspace(object workspace)
    {
        if (workspace is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void UnsubscribeVmEventHandlers()
    {
        Pages.CollectionChanged -= OnPagesChanged;
        PreferencesPage.ProcessingPreferenceChangeCommitted -= OnProcessingPreferenceChangeCommitted;
        UnsubscribeSignalPreferenceChanges();
        NotesPage.ForkSettings.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
        NotesPage.ShockSettings.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
        NotesPage.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
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
    private void SelectAnalysisRange(TelemetryRangeSelection? selection)
    {
        if (selection is null)
        {
            editorActions.ClearAnalysisSelection();
            return;
        }

        editorActions.SelectAnalysisRange(selection);
    }

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        if (!TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range))
        {
            return;
        }

        editorActions.SetAnalysisRange(range);
    }

    public void ClearAnalysisRange()
    {
        if (currentEditorState.Intent.AnalysisRange is null &&
            currentEditorState.Intent.PendingAnalysisRangeBoundary is null)
        {
            return;
        }

        editorActions.ClearAnalysisRange();
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        editorActions.SetAnalysisRangeBoundary(boundarySeconds);
    }

    private void SetAnalysisRangeStartBoundary(double boundarySeconds)
    {
        editorActions.SetAnalysisRangeStartBoundary(boundarySeconds);
    }

    private void SetAnalysisRangeEndBoundary(double boundarySeconds)
    {
        editorActions.SetAnalysisRangeEndBoundary(boundarySeconds);
    }

    public void SetAnalysisRangeBoundaryFromMarker(double markerSeconds)
    {
        SetAnalysisRangeBoundary(markerSeconds);
    }

    [RelayCommand]
    private async Task Loaded(Rect? bounds = null)
    {
        var wasLoaded = viewLoaded;
        viewLoaded = true;
        var loadLifecycleToken = wasLoaded
            ? CancellationToken.None
            : viewLoadOperation.Start();
        processedTelemetryRetention ??= processedTelemetryReader.Retain(Id);
        var dimensions = CreatePresentationDimensions(bounds);
        if (dimensions is not null)
        {
            lastPresentationDimensions = dimensions;
        }

        if (wasLoaded)
        {
            PublishRecordedSessionHostRuntimeChange();
            return;
        }

        PublishRecordedSessionHostRuntimeChange();

        // Subscribe before the awaited restore so a remote sync apply that
        // lands while restore is in flight is not missed.
        var watch = recordedSessionProjection.WatchSession(Id);
        if (SynchronizationContext.Current is { } synchronizationContext)
        {
            watch = watch.ObserveOn(synchronizationContext);
        }
        EnsureScopedSubscription(s =>
        {
            s.Add(recordedPreferenceStore.Observe().Subscribe(OnSyncedPreferencesArrived));
            s.Add(watch.Subscribe(snapshot => PublishEditorInput(domainInput, snapshot)));
        });
        if (replayStalenessOnNextLoad)
        {
            replayStalenessOnNextLoad = false;
            PublishEditorInput(stalenessReplayInput, Unit.Default);
        }

        try
        {
            await InitializeRecordedSessionExtensionsAsync(loadLifecycleToken);
            if (!viewLoaded || loadLifecycleToken.IsCancellationRequested)
            {
                return;
            }

            await RestoreRecordedPreferencesAsync(loadLifecycleToken);
            if (!viewLoaded || loadLifecycleToken.IsCancellationRequested)
            {
                return;
            }

            await RequestLoadAsync();
        }
        catch (OperationCanceledException) when (loadLifecycleToken.IsCancellationRequested)
        {
        }
    }

    protected override void OnActivated()
    {
        hasBeenActivated = true;
        PublishRecordedSessionHostRuntimeChange();

        if (!viewLoaded)
        {
            return;
        }

        _ = HandleDeferredDomainAsync();
    }

    protected override void OnDeactivated()
    {
        PublishRecordedSessionHostRuntimeChange();
    }

    private bool ShouldDeferDomainHandling() =>
        deferDomainHandlingWhenInactive &&
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
        if (layoutProfileTransitionState.IsTransitioning)
        {
            return;
        }

        await StopLoadedSessionAsync();
    }

    private async Task StopLoadedSessionAsync()
    {
        viewLoaded = false;
        viewLoadOperation.Cancel();
        loadOperation.Cancel();
        observedInitialDomain = false;
        replayStalenessOnNextLoad = true;
        deferredDomain = null;
        stalenessReconciler.ResetForUnload();
        PublishRecordedSessionHostRuntimeChange();
        await DisposeRecordedSessionExtensionScopesAsync();
        DisposeProcessedTelemetryRetention();
        DisposeScopedSubscriptions();
    }

    #endregion

}
