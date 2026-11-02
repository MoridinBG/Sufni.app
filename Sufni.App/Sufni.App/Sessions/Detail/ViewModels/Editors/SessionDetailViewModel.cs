using Serilog;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Models;
using Sufni.App.Video.Coordinators;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
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
using Sufni.App.Sessions.Services;
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
using Sufni.App.Video.Models;
using Sufni.App.Video.ViewModels;
using Sufni.App.Segments.ViewModels;
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
    private readonly ISessionLoader sessionLoader;
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly ISessionStore sessionStore;
    private readonly IRecordedSessionProjection recordedSessionProjection;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly ObservableCollection<PageViewModelBase> pages = [];
    private readonly SessionTimelineLinkViewModel timeline = new();
    private readonly VideoPaneViewModel? videoPane;
    private readonly SegmentPickerViewModel segmentPicker;
    private readonly SegmentComparisonViewModel segmentComparison;
    private readonly TelemetrySourceVisibilityStore sourceVisibility = new();
    private readonly ISessionOperationRunner sessionOperationRunner;
    private static readonly ILogger logger = Log.ForContext<SessionDetailViewModel>();
    private SessionStalenessReconciliation.Required? promptedRecompute;
    private bool recomputePromptRunning;
    private bool reportedUnavailableInputs;
    private Task pendingDomainReactions = Task.CompletedTask;
    private readonly IRecordedSessionAnalysisResultState analysisResultState;
    private readonly IDisposable analysisResultSubscription;
    private readonly RecordedSessionEditorActions editorActions = new();
    private readonly IDisposable editorStateSubscription;
    private readonly RecordedSessionEditorEffects editorEffects;
    private readonly Subject<int> pageCountInput = new();
    private readonly Subject<SessionPreferences> preferenceReplayInput = new();
    private readonly Subject<RecordedSessionLoadPresentation> loadPresentationInput = new();
    private readonly Subject<SessionOperationPresentationState> sessionOperationStateInput = new();
    private readonly Subject<SessionDampingPercentages> dampingPercentagesInput = new();
    private readonly Subject<DampingSpeedCutoffs> plotDampingSpeedCutoffsInput = new();
    private readonly Subject<bool> canEditDampingSpeedCutoffsInput = new();
    private readonly Subject<SessionInsightsResult> sessionInsightsInput = new();
    private readonly Subject<AnalysisSelectionState> analysisSelectionInput = new();
    private readonly Subject<RecordedSessionDomainSnapshot> domainInput = new();
    private readonly Subject<Unit> dirtyBaselineInput = new();
    private readonly Subject<IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>> signalPlotContextMenuActionsInput = new();
    private readonly CompositeDisposable editorInputSubjects;
    private readonly RecordedSessionEditorStateController editorStateController;
    private RecordedSessionEditorState currentEditorState = RecordedSessionEditorState.CreateInitial();
    private RecordedSessionDomainSnapshot? handledDomain;
    private RecordedSessionDomainSnapshot? deferredDomain;
    private readonly RecordedPagePresentationApplier presentationApplier;
    private readonly ISessionPreferences sessionPreferences;
    private Task pendingPreferenceOperations = Task.CompletedTask;
    private readonly ProcessingPreferenceWorkflow processingPreferenceWorkflow;
    private SessionDraft draftBase;
    private RecordedSignalsPageViewModel SignalsPage { get; }
    private StrokesPageViewModel StrokesPage { get; }
    private SpringPageViewModel SpringPage { get; }
    private BalancePageViewModel BalancePage { get; }
    private VibrationPageViewModel VibrationPage { get; }
    private PositionVelocityPageViewModel PositionVelocityPage { get; }
    private SessionInsightsPageViewModel AnalysisPage { get; }

    private readonly CancellableOperation loadOperation = new();
    private readonly CancellableOperation viewLoadOperation = new();
    private TimelineAlignmentMark? pendingTimelineAlignmentMark;
    private RecordedSessionAnalysisInputs analysisInputs;
    private readonly AnalysisRequestScheduler analysisRequestScheduler;
    private int telemetryGeneration;
    private bool suppressInsightsRecompute;
    private bool suppressAnalysisRecompute;
    private enum EditorLifecycle { Created, Loaded, ApplyingDraft, Closed }
    private EditorLifecycle lifecycle;
    private bool IsLoaded => lifecycle == EditorLifecycle.Loaded;
    private Task? closeTask;
    private readonly SignalRowActionsController signalRowActions;
    private readonly SignalAutozoomController signalAutozoomController;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> setAnalysisRangeStartCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> setAnalysisRangeEndCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> clearAnalysisRangeFromContextCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> markGpsEventCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markGpsTelemetryEventCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> cancelGpsTimelineAlignmentCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> markVideoEventCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markVideoTelemetryEventCommand;
    private readonly IRelayCommand<TelemetryPlotContextMenuContext?> cancelVideoAlignmentCommand;
    private readonly ISessionEditingCoordinator sessionEditingCoordinator;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> trimFromBeginningCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> trimUntilEndCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> splitCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> undoTrimCommand;
    private readonly IAsyncRelayCommand<TelemetryPlotContextMenuContext?> saveSelectionAsSegmentCommand;
    private readonly DampingCutoffWorkflow dampingCutoffWorkflow;
    private readonly bool deferDomainHandlingWhenInactive;
    private IDisposable? processedTelemetryRetention;
    private TelemetryData? telemetryData;
    private List<TrackPoint>? trackPoints;
    private MapViewModel? mapViewModel;
    private bool mapInitializeRequested;

    #endregion Private fields

    #region Public fields

    public DampingPageViewModel DampingPage { get; }
    public NotesPageViewModel NotesPage { get; } = new();
    public SignalDisplayPreferences SignalDisplayPreferences
    {
        get => currentEditorState.Preferences.SignalDisplay;
    }

    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => currentEditorState.Preferences.SignalLayout;
        set => editorActions.SetSignalLayoutPreferences(value);
    }

    public SessionLayoutPreferences LayoutPreferences
    {
        get => currentEditorState.Preferences.Layout;
        set => editorActions.SetLayoutPreferences(value);
    }

    public SessionPaneGroupPreferences? MediaLayoutPreferences
    {
        get => currentEditorState.Preferences.Layout.DesktopMediaRows;
        set => LayoutPreferences = LayoutPreferences with { DesktopMediaRows = value };
    }

    public TelemetrySourceVisibilityStore SourceVisibility => sourceVisibility;
    public PreferencesPageViewModel PreferencesPage { get; }
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

    #endregion Public fields

    #region Observable properties

    public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } = SessionInsightsPresentation.TravelDistributionModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionInsightsPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionInsightsPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionInsightsPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } = SessionInsightsPresentation.SessionInsightsTargetProfileOptions;
    public string SessionAnalysisRangeText => currentEditorState.Intent.AnalysisRange is { } range
        ? $"Selected range {FormatSeconds(range.StartSeconds)}-{FormatSeconds(range.EndSeconds)}s"
        : "Full session";
    public string SessionAnalysisModesText => SessionInsightsPresentation.DescribeModes(
        currentEditorState.Preferences.Analysis.TravelDistributionMode,
        currentEditorState.Preferences.Analysis.VelocityAverageMode,
        currentEditorState.Preferences.Analysis.BalanceDisplacementMode,
        currentEditorState.Preferences.Analysis.BalanceSpeedMode);
    public ObservableCollection<PageViewModelBase> Pages => pages;
    public SessionScreenPresentationState ScreenState => currentEditorState.Presentation.ScreenState;
    public SessionOperationPresentationState SessionOperationState => currentEditorState.Presentation.OperationState;

    #endregion Observable properties

    #region Private methods

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
            currentEditorState.Preferences.Analysis.TravelDistributionMode,
            currentEditorState.Preferences.Analysis.VelocityAverageMode,
            currentEditorState.Preferences.Analysis.BalanceDisplacementMode,
            currentEditorState.Preferences.Analysis.BalanceSpeedMode,
            currentEditorState.Intent.DampingSpeedCutoffs,
            currentEditorState.Presentation.DampingPercentages,
            currentEditorState.Preferences.Analysis.SessionInsightsTargetProfile);

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
            ErrorMessages.Add(new FailurePresentation($"Failed to update session analysis: {change.Error.Message}"));
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


    internal TelemetryData? CurrentTelemetryData => currentEditorState.TelemetryData;

    internal IReadOnlyList<TrackPoint>? CurrentTrackPoints => currentEditorState.TrackPoints;


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
        IRelayCommand<TelemetryPlotContextMenuContext?> cancelGpsTimelineAlignmentCommand,
        IRelayCommand<TelemetryPlotContextMenuContext?> markVideoEventCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> markVideoTelemetryEventCommand,
        IRelayCommand<TelemetryPlotContextMenuContext?> cancelVideoAlignmentCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> trimFromBeginningCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> trimUntilEndCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> splitCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> undoTrimCommand,
        IAsyncRelayCommand<TelemetryPlotContextMenuContext?> saveSelectionAsSegmentCommand)
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
        var markVideoEvent = new TelemetryPlotContextMenuAction(
            "video-mark-video-event",
            "Mark video event here",
            markVideoEventCommand);
        var markVideoTelemetryEvent = new TelemetryPlotContextMenuAction(
            "video-mark-telemetry-event",
            "Mark telemetry event here",
            markVideoTelemetryEventCommand);
        var cancelVideoAlignment = new TelemetryPlotContextMenuAction(
            "video-cancel-alignment",
            "Cancel video alignment",
            cancelVideoAlignmentCommand);
        var trimFromBeginning = new TelemetryPlotContextMenuAction(
            "session-trim-from-beginning",
            "Trim from beginning",
            trimFromBeginningCommand);
        var trimUntilEnd = new TelemetryPlotContextMenuAction(
            "session-trim-until-end",
            "Trim until end",
            trimUntilEndCommand);
        var split = new TelemetryPlotContextMenuAction(
            "session-split-here",
            "Split here",
            splitCommand);
        var undoTrim = new TelemetryPlotContextMenuAction(
            "session-undo-trim",
            "Undo trim",
            undoTrimCommand);
        var saveSelectionAsSegment = new TelemetryPlotContextMenuAction(
            "save-current-selection",
            "Save current selection",
            saveSelectionAsSegmentCommand);
        TelemetryPlotContextMenuAction[] actions =
        [
            setAnalysisRangeStart,
            setAnalysisRangeEnd,
            clearAnalysisRange,
            markGpsEvent,
            markGpsTelemetryEvent,
            cancelGpsTimelineAlignment,
            markVideoEvent,
            markVideoTelemetryEvent,
            cancelVideoAlignment,
            trimFromBeginning,
            trimUntilEnd,
            split,
            undoTrim,
            saveSelectionAsSegment,
        ];

        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
        {
            [SignalRowIds.Travel] = AppendContextMenuActions(baseActions, SignalRowIds.Travel, actions),
            [SignalRowIds.Velocity] = AppendContextMenuActions(baseActions, SignalRowIds.Velocity, actions),
            [SignalRowIds.Imu] = AppendContextMenuActions(baseActions, SignalRowIds.Imu, actions),
            [SignalRowIds.PitchRoll] = AppendContextMenuActions(baseActions, SignalRowIds.PitchRoll, actions),
            [SignalRowIds.Speed] = AppendContextMenuActions(baseActions, SignalRowIds.Speed, actions),
            [SignalRowIds.Elevation] = AppendContextMenuActions(baseActions, SignalRowIds.Elevation, actions),
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
        if (!IsLoaded)
        {
            return;
        }

        var token = loadOperation.Start();
        var currentSnapshot = sessionStore.Get(Id);
        var mapExpected = currentEditorState.Domain?.SelectedTrack is not null;
        if (currentSnapshot?.HasProcessedData == true)
        {
            presentationApplier.ClearRecordedPresentation();
            presentationApplier.ApplyRecordedLoadingStates();
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
            var progress = new SessionLoadProgressReporter(
                update => PublishSessionLoadProgress(update, mapExpected, currentSnapshot, token));
            var result = await sessionLoader.LoadDetailAsync(Id, progress, token);
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

            if (result is not SessionDetailLoadResult.Loaded loaded)
            {
                return;
            }

            var trackResult = await sessionLoader.LoadTrackAsync(
                Id,
                loaded.Data.TelemetryPresentation.TelemetryData,
                progress,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var settledResult = CreateSettledLoadResult(loaded, trackResult);
            await PublishSessionLoadProgressAsync(
                SessionDetailLoadProgress.ApplyingSessionData,
                mapExpected,
                sessionStore.Get(Id) ?? currentSnapshot,
                token);
            PublishLoadResultPresentation(CreateLoadPresentation(
                settledResult,
                sessionStore.Get(Id) ?? currentSnapshot));
            if (settledResult is not SessionDetailLoadResult.Loaded)
            {
                presentationApplier.ApplyLoadResult(settledResult);
            }
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
        if (token.IsCancellationRequested || !IsLoaded)
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
        if (token.IsCancellationRequested || !IsLoaded)
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
        if (token.IsCancellationRequested ||
            !IsLoaded ||
            currentEditorState.Load is RecordedSessionLoadPresentation.Loaded)
        {
            return;
        }

        PublishLoadPresentation(new RecordedSessionLoadPresentation.Loading(
            mapExpected,
            progress,
            currentEditorState.Session ?? fallbackSnapshot));
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

    private static SessionDetailLoadResult CreateSettledLoadResult(
        SessionDetailLoadResult.Loaded initialResult,
        SessionDetailTrackLoadResult trackResult)
    {
        return trackResult switch
        {
            SessionDetailTrackLoadResult.Loaded loaded =>
                new SessionDetailLoadResult.Loaded(
                    new SessionDetailData(
                        initialResult.Data.TelemetryPresentation with
                        {
                            FullTrackId = loaded.Data.FullTrackId,
                            FullTrackPoints = loaded.Data.FullTrackPoints,
                            TrackPoints = loaded.Data.TrackPoints,
                        })),
            SessionDetailTrackLoadResult.Failed failed =>
                new SessionDetailLoadResult.Failed(failed.ErrorMessage),
            _ => throw new InvalidOperationException("Unsupported session track load result."),
        };
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
        draftBase = SessionDraft.From(snapshot);
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
        try
        {
            await OnDomainChangedCoreAsync(domain);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            logger.Error(exception, "Session editor reconciliation failed for {SessionId}", Id);
            if (IsLoaded) ErrorMessages.Add(FailurePresentation.From(SessionEditorFailureReason.Reconcile, exception.Message));
        }
    }

    private async Task OnDomainChangedCoreAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (ShouldDeferDomainHandling())
        {
            deferredDomain = domain;
            return;
        }

        deferredDomain = null;

        var previous = handledDomain;
        handledDomain = domain;

        if (previous is not null)
        {
            // The initial replay already matches the loaded state, so only real
            // changes drive the derived/metadata reaction.
            await ApplyDomainReactionAsync(previous, domain);
        }

        if (IsLoaded) await ReconcileStalenessAsync(domain);
    }

    private async Task ReconcileStalenessAsync(RecordedSessionDomainSnapshot domain)
    {
        var result = await sessionCoordinator.ReconcileStalenessAsync(domain);
        if (!IsLoaded) return;
        switch (result)
        {
            case SessionStalenessReconciliation.Current:
                promptedRecompute = null;
                return;
            case SessionStalenessReconciliation.Unavailable unavailable:
                ReportUnavailableInputs(unavailable.Reason);
                return;
            case SessionStalenessReconciliation.Required required:
                if (recomputePromptRunning || promptedRecompute == required) return;
                promptedRecompute = required;
                break;
            default:
                return;
        }

        recomputePromptRunning = true;
        try
        {
            var choice = await dialogService.ShowChoiceAsync(
                string.IsNullOrWhiteSpace(domain.Session.Name) ? "Session has to be recomputed" : $"Session {domain.Session.Name} has to be recomputed",
                IsDirty ? "Recompute this session now? Your unsaved changes will be kept." : "Recompute this session now?",
                [new DialogChoice("cancel", "Cancel"), new DialogChoice("recompute-all", "Recompute all"),
                    new DialogChoice("recompute-this", "Recompute", IsDefault: true)]);
            if (!IsLoaded) return;
            var current = currentEditorState.Domain ?? domain;
            if (choice == "recompute-all")
            {
                await dialogService.ShowProgressAsync("Recomputing all sessions", progress =>
                    sessionCoordinator.ReconcileStalenessAsync(current, SessionRecomputeChoice.AllSessions,
                        new UiThreadProgress<SessionRecomputeAllProgress>(UiThreadDispatcher, report =>
                            progress.Report(new DialogProgress(report.Total == 0 ? 1 : (double)report.Completed / report.Total,
                                $"{report.Completed} of {report.Total} sessions")))));
                return;
            }
            if (choice != "recompute-this") return;
            result = await sessionCoordinator.ReconcileStalenessAsync(current, SessionRecomputeChoice.ThisSession);
            if (!IsLoaded) return;
            switch (result)
            {
                case SessionStalenessReconciliation.Unavailable unavailable:
                    ReportUnavailableInputs(unavailable.Reason);
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.NotRecomputable unavailable }:
                    ReportUnavailableInputs(unavailable.Reason);
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.ProcessingFailed failed }:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.Failed failed }:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.Missing }:
                    Notifications.Add("The session is no longer available.");
                    break;
            }
        }
        finally
        {
            recomputePromptRunning = false;
        }
    }

    private void ReportUnavailableInputs(SessionStaleness reason)
    {
        if (reportedUnavailableInputs) return;
        reportedUnavailableInputs = true;
        ErrorMessages.Add(new FailurePresentation(reason is SessionStaleness.MissingDependencies
            ? "Session cannot be recomputed until its missing setup or bike is restored."
            : "The source recording is unavailable. Existing processed data can still be viewed."));
    }

    private async Task ApplyDomainReactionAsync(RecordedSessionDomainSnapshot previous, RecordedSessionDomainSnapshot current)
    {
        var snapshot = current.Session;
        var metadataChanged = RecordedSessionEditorEffects.RequiresDraftReconciliation(previous.Session, snapshot);
        var reloadTelemetry = RecordedSessionEditorEffects.RequiresTelemetryReload(previous, current);

        if (metadataChanged && IsDirty)
        {
            // Refresh derived telemetry even while the discard prompt is pending so
            // plots never show stale data; unsaved edits are preserved.
            if (reloadTelemetry)
            {
                await RequestLoadAsync();
            }

            var discard = await dialogService.ShowConfirmationAsync(
                "Session changed elsewhere",
                "This session has been updated from another source. Discard your changes and reload?");
            if (!IsLoaded) return;
            if (discard)
            {
                await ApplyPersistedSnapshotAsync(snapshot);
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

        if (reloadTelemetry)
        {
            await RequestLoadAsync();
        }
    }

    private Task HandleDeferredDomainAsync()
    {
        if (!IsLoaded || deferredDomain is not { } domain)
        {
            return Task.CompletedTask;
        }

        deferredDomain = null;
        return OnDomainChangedAsync(domain);
    }

    private void PublishEditorInput<T>(Subject<T> input, T value)
    {
        if (lifecycle != EditorLifecycle.Closed)
        {
            input.OnNext(value);
        }
    }

    private void DisposeProcessedTelemetryRetention()
    {
        processedTelemetryRetention?.Dispose();
        processedTelemetryRetention = null;
    }

    private void SetPendingTimelineAlignmentMark(TimelineAlignmentMark? mark)
    {
        pendingTimelineAlignmentMark = mark;
        NotifyTimelineAlignmentCommandsCanExecuteChanged();
    }

    private void NotifyTimelineAlignmentCommandsCanExecuteChanged()
    {
        markGpsEventCommand.NotifyCanExecuteChanged();
        markGpsTelemetryEventCommand.NotifyCanExecuteChanged();
        cancelGpsTimelineAlignmentCommand.NotifyCanExecuteChanged();
        markVideoEventCommand.NotifyCanExecuteChanged();
        markVideoTelemetryEventCommand.NotifyCanExecuteChanged();
        cancelVideoAlignmentCommand.NotifyCanExecuteChanged();
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

        SetPendingTimelineAlignmentMark(new TimelineAlignmentMark.GpsTrack(context!.ClickSeconds));
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
               pendingTimelineAlignmentMark is TimelineAlignmentMark.GpsTrack &&
               IsTelemetryPlotContext(context);
    }

    private async Task MarkGpsTelemetryEventFromPlotContextAsync(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkGpsTelemetryEventFromPlotContext(context) ||
            pendingTimelineAlignmentMark is not TimelineAlignmentMark.GpsTrack mark ||
            telemetryData is null)
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
        var offsetDeltaSeconds = mark.Seconds - context!.ClickSeconds;
        var applied = await sessionCoordinator.ShiftGpsOffsetAsync(Id, offsetDeltaSeconds);

        switch (applied)
        {
            case GpsOffsetResult.NoMatchingTrack:
                Notifications.Add("GPS offset could not be applied: no matching track segment was found.");
                break;
            case GpsOffsetResult.SessionMissing:
                Notifications.Add("The session is no longer available.");
                break;
            case GpsOffsetResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }

        // One-way: on success the store upsert drives the refreshed track and
        // baseline through the session-detail watch reaction, like recompute.
    }

    private bool CanCancelGpsTimelineAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is TimelineAlignmentMark.GpsTrack &&
               IsTelemetryPlotContext(context);
    }

    private void CancelGpsTimelineAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanCancelGpsTimelineAlignmentFromPlotContext(context))
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
    }

    private bool CanMarkVideoEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is null &&
               IsTelemetryPlotContext(context) &&
               videoPane?.ClipAt(context!.ClickSeconds) is not null;
    }

    private void MarkVideoEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkVideoEventFromPlotContext(context) ||
            videoPane?.ClipAt(context!.ClickSeconds) is not { } clip)
        {
            return;
        }

        SetPendingTimelineAlignmentMark(new TimelineAlignmentMark.VideoClip(clip.Id, context.ClickSeconds));
    }

    private bool CanMarkVideoTelemetryEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is TimelineAlignmentMark.VideoClip mark &&
               videoPane?.HasPresentClip(mark.ClipId) == true &&
               IsTelemetryPlotContext(context);
    }

    private async Task MarkVideoTelemetryEventFromPlotContextAsync(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkVideoTelemetryEventFromPlotContext(context) ||
            pendingTimelineAlignmentMark is not TimelineAlignmentMark.VideoClip mark)
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
        switch (await videoPane!.ShiftAlignmentAsync(mark.ClipId, mark.Seconds - context!.ClickSeconds))
        {
            case VideoAlignmentResult.ClipMissing:
                Notifications.Add("Session video alignment could not be saved because the video metadata is missing.");
                break;
            case VideoAlignmentResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private bool CanCancelVideoAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is TimelineAlignmentMark.VideoClip &&
               IsTelemetryPlotContext(context);
    }

    private void CancelVideoAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanCancelVideoAlignmentFromPlotContext(context))
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
    }

    private bool IsSessionEditRunning =>
        trimFromBeginningCommand.IsRunning ||
        trimUntilEndCommand.IsRunning ||
        splitCommand.IsRunning ||
        undoTrimCommand.IsRunning;

    private bool CanEditWindowAt(TelemetryPlotContextMenuContext? context, SessionWindowEditKind kind) =>
        !IsSessionEditRunning && IsTelemetryPlotContext(context) && telemetryData is { } telemetry &&
        SessionWindowEditPolicy.CanEditAt(kind, context!.ClickSeconds, context.DurationSeconds, telemetry.Metadata.SampleRate);

    private bool CanTrimFromBeginningFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        CanEditWindowAt(context, SessionWindowEditKind.TrimFromBeginning);

    private bool CanTrimUntilEndFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        CanEditWindowAt(context, SessionWindowEditKind.TrimUntilEnd);

    private bool CanSplitFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        CanEditWindowAt(context, SessionWindowEditKind.Split);

    private bool CanUndoTrimFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        !IsSessionEditRunning &&
        IsTelemetryPlotContext(context) &&
        currentEditorState.Session is { CanUndoTrim: true };

    private Task TrimFromBeginningFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanTrimFromBeginningFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session trimmed.",
                () => sessionEditingCoordinator.TrimFromBeginningAsync(Id, context!.ClickSeconds))
            : Task.CompletedTask;

    private Task TrimUntilEndFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanTrimUntilEndFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session trimmed.",
                () => sessionEditingCoordinator.TrimUntilEndAsync(Id, context!.ClickSeconds))
            : Task.CompletedTask;

    private Task SplitFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanSplitFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session split.",
                () => sessionEditingCoordinator.SplitAsync(Id, context!.ClickSeconds,
                    new UiThreadProgress<SessionEditProgress>(UiThreadDispatcher,
                        progress => shell.OpenEditor(new EditorKey.Session(progress.CreatedSessionId), background: true))))
            : Task.CompletedTask;

    private Task UndoTrimFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanUndoTrimFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session trim restored.",
                () => sessionEditingCoordinator.UndoTrimAsync(Id))
            : Task.CompletedTask;

    private bool CanSaveSelectionAsSegmentFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        telemetryData is not null &&
        IsTelemetryPlotContext(context) &&
        context!.IsClickInsideAnalysisRange;

    private Task SaveSelectionAsSegmentFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanSaveSelectionAsSegmentFromPlotContext(context)
            ? segmentPicker.SaveSelectionAsync(context!.AnalysisRange!.Value)
            : Task.CompletedTask;

    private async Task RunSessionEditAsync(string appliedMessage, Func<Task<SessionEditResult>> edit)
    {
        switch (await edit())
        {
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.Failed failed }:
                ShowRecomputeFailure(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.ProcessingFailed failed }:
                ShowRecomputeFailure(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.Missing }:
                Notifications.Add("The session is no longer available.");
                break;
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.NotRecomputable }:
                Notifications.Add("The edit was applied, but the session cannot be recomputed until its source and dependencies are available.");
                break;
            case SessionEditResult.Applied:
                Notifications.Add(appliedMessage);
                break;
            case SessionEditResult.Missing:
                Notifications.Add("The session is no longer available.");
                break;
            case SessionEditResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
        void ShowRecomputeFailure(FailurePresentation failure) =>
            ErrorMessages.Add(new FailurePresentation($"The edit was applied. {failure.Summary}", failure.Details));
    }

    void ISessionOperationGateway.AddError(FailurePresentation failure) => ErrorMessages.Add(failure);

    void ISessionOperationGateway.Notify(string message) => Notifications.Add(message);

    Guid ISessionOperationGateway.SessionId => Id;

    bool ISessionOperationGateway.IsDirty => IsDirty;

    bool ISessionOperationGateway.IsViewLoaded => IsLoaded;

    bool ISessionOperationGateway.ShouldDeferDomainHandling() => ShouldDeferDomainHandling();

    void ISessionOperationGateway.SetSignalLayoutPreferences(SignalLayoutPreferences preferences) =>
        SignalLayoutPreferences = preferences;

    void ISessionOperationGateway.RequestSessionInsights() => RecomputeSessionInsights();

    #endregion

    #region Constructors

    internal SessionDetailViewModel(
        SessionSnapshot snapshot,
        ISessionCoordinator sessionCoordinator,
        ISessionLoader sessionLoader,
        ISessionStore sessionStore,
        IRecordedSessionProjection recordedSessionProjection,
        IMapViewModelFactory mapViewModelFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISessionPreferences sessionPreferences,
        IUiThreadDispatcher uiThreadDispatcher,
        bool deferDomainHandlingWhenInactive,
        ISessionProcessedTelemetryReader processedTelemetryReader,
        IRecordedSessionAnalysisResultStateFactory analysisResultStateFactory,
        ISessionEditingCoordinator sessionEditingCoordinator,
        ISegmentPickerFactory segmentPickerFactory,
        ISegmentComparisonFactory segmentComparisonFactory,
        ISessionOperationRunner sessionOperationRunner,
        TimeProvider timeProvider,
        IBikeCoordinator? bikeCoordinator = null,
        IVideoPaneFactory? videoPaneFactory = null,
        TrackSnapshot? initialTrack = null)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sessionPreferences);
        this.deferDomainHandlingWhenInactive = deferDomainHandlingWhenInactive;

        this.sessionCoordinator = sessionCoordinator;
        this.sessionPreferences = sessionPreferences;
        PreferencesPage = new PreferencesPageViewModel(() => currentEditorState.Preferences.SignalDisplay, editorActions.SetSignalDisplayPreferences);
        this.sessionLoader = sessionLoader;
        this.sessionOperationRunner = sessionOperationRunner;
        this.bikeCoordinator = bikeCoordinator;
        this.sessionStore = sessionStore;
        this.recordedSessionProjection = recordedSessionProjection;
        this.processedTelemetryReader = processedTelemetryReader;
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
            dampingPercentagesInput,
            plotDampingSpeedCutoffsInput,
            canEditDampingSpeedCutoffsInput,
            sessionInsightsInput,
            analysisSelectionInput,
            signalPlotContextMenuActionsInput,
            domainInput,
            dirtyBaselineInput);
        editorStateController = new RecordedSessionEditorStateController(
            new RecordedSessionEditorStateInputs(
                editorActions.Intents,
                pageCountInput,
                preferenceReplayInput,
                loadPresentationInput,
                sessionOperationStateInput,
                dampingPercentagesInput,
                plotDampingSpeedCutoffsInput,
                canEditDampingSpeedCutoffsInput,
                sessionInsightsInput,
                analysisSelectionInput,
                signalPlotContextMenuActionsInput,
                domainInput));
        signalRowActions = new SignalRowActionsController(() => currentEditorState.Presentation.Signals, editorActions);
        editorStateSubscription = editorStateController.State.Subscribe(ApplyEditorState);
        segmentComparison = segmentComparisonFactory.Create(
            snapshot.Id,
            editorStateController.State.Select(state => new SegmentComparisonSessionState(
                state.Intent.AnalysisRange,
                state.TelemetryData,
                state.TrackTimelineContext,
                state.Preferences.Analysis.TravelDistributionMode,
                state.Preferences.Analysis.VelocityAverageMode,
                state.Intent.DampingSpeedCutoffs,
                state.Presentation.DampingPercentages)),
            (start, end) => Timeline.SetVisibleRange(start, end, this),
            message => ErrorMessages.Add(new FailurePresentation(message)),
            Notifications.Add);
        this.sessionEditingCoordinator = sessionEditingCoordinator;
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
        markVideoEventCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            MarkVideoEventFromPlotContext,
            CanMarkVideoEventFromPlotContext);
        markVideoTelemetryEventCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            MarkVideoTelemetryEventFromPlotContextAsync,
            CanMarkVideoTelemetryEventFromPlotContext);
        cancelVideoAlignmentCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            CancelVideoAlignmentFromPlotContext,
            CanCancelVideoAlignmentFromPlotContext);
        trimFromBeginningCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            TrimFromBeginningFromPlotContextAsync,
            CanTrimFromBeginningFromPlotContext);
        trimUntilEndCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            TrimUntilEndFromPlotContextAsync,
            CanTrimUntilEndFromPlotContext);
        splitCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            SplitFromPlotContextAsync,
            CanSplitFromPlotContext);
        undoTrimCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            UndoTrimFromPlotContextAsync,
            CanUndoTrimFromPlotContext);
        saveSelectionAsSegmentCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            SaveSelectionAsSegmentFromPlotContextAsync,
            CanSaveSelectionAsSegmentFromPlotContext);
        SignalPlotContextMenuActionsBySignalRowId = CreateSignalPlotContextMenuActionsBySignalRowId(
            signalAutozoomController.ActionsBySignalRowId,
            setAnalysisRangeStartCommand,
            setAnalysisRangeEndCommand,
            clearAnalysisRangeFromContextCommand,
            markGpsEventCommand,
            markGpsTelemetryEventCommand,
            cancelGpsTimelineAlignmentCommand,
            markVideoEventCommand,
            markVideoTelemetryEventCommand,
            cancelVideoAlignmentCommand,
            trimFromBeginningCommand,
            trimUntilEndCommand,
            splitCommand,
            undoTrimCommand,
            saveSelectionAsSegmentCommand);
        PublishEditorInput(signalPlotContextMenuActionsInput, SignalPlotContextMenuActionsBySignalRowId);
        draftBase = SessionDraft.From(snapshot);
        Id = snapshot.Id;
        segmentPicker = segmentPickerFactory.Create(
            Id,
            editorStateController.State.Select(state => new SegmentPickerSessionState(
                state.Session?.Timestamp,
                state.TelemetryData?.Metadata.Duration,
                state.Intent.AnalysisRange,
                state.Domain?.Source?.SourceHash,
                state.Session?.WindowStartSeconds ?? 0)),
            range =>
            {
                if (range is { } value)
                {
                    editorActions.SetAnalysisRange(value);
                }
                else
                {
                    editorActions.ClearAnalysisRange();
                }
            },
            ErrorMessages.Add,
            Notifications.Add);
        MobileWorkspace = new SessionShellMobileWorkspaceViewModel(
            this,
            Pages,
            editorStateController.State,
            editorActions);
        SignalsWorkspace = new RecordedSessionSignalsWorkspaceViewModel(
            editorStateController.State,
            analysisResultState,
            SourceVisibility,
            Timeline,
            editorActions,
            segmentPicker,
            segmentComparison);
        videoPane = videoPaneFactory?.Create(
            Timeline,
            editorStateController.State.Select(state => new VideoSessionTiming(
                state.Domain?.Source?.SourceHash,
                state.Session?.WindowStartSeconds ?? 0,
                state.TelemetryData?.Metadata.Duration ?? state.Session?.DurationSeconds)));
        mapViewModel = mapViewModelFactory.Create();
        MediaWorkspace = new SessionMediaWorkspaceViewModel(
            editorStateController.State,
            mapViewModel,
            Timeline,
            videoPane,
            segmentComparison);
        var analysisWorkspace = new SessionAnalysisWorkspaceViewModel(
            editorStateController.State,
            this,
            editorActions,
            SelectAnalysisRangeCommand,
            analysisResultState,
            segmentComparison);
        AnalysisWorkspace = analysisWorkspace;
        PositionVelocityPage = analysisWorkspace.PositionVelocity;
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
        processingPreferenceWorkflow = new ProcessingPreferenceWorkflow(
            sessionStore,
            PreferencesPage,
            sessionCoordinator,
            this);
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
        Pages.Add(PositionVelocityPage);
        Pages.Add(BalancePage);
        Pages.Add(VibrationPage);
        Pages.Add(AnalysisPage);
        Pages.Add(NotesPage);
        Pages.Add(PreferencesPage);
        Pages.CollectionChanged += OnPagesChanged;
        PublishEditorInput(pageCountInput, Pages.Count);
        if (snapshot.HasProcessedData)
        {
            presentationApplier.ApplyRecordedLoadingStates();
            PublishLoadPresentation(new RecordedSessionLoadPresentation.Loading(
                initialTrack is not null,
                SessionDetailLoadProgress.PreparingSession,
                snapshot));
        }

        NotesPage.ForkSettings.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        NotesPage.ShockSettings.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        NotesPage.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        PreferencesPage.ProcessingPreferenceChangeCommitted += OnProcessingPreferenceChangeCommitted;

        ResetImplementation();
        SetSignalPresentationState(CreateSignalPresentationState());
        if (!snapshot.HasProcessedData)
        {
            PublishLoadPresentation(new RecordedSessionLoadPresentation.Empty(snapshot));
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
                RecordedSessionEditorEffects.DirtyBaselineTracking(dirtyBaselineInput),
            ],
            ApplyRecordedSessionEditorEffect);
    }

    #endregion

    #region Private methods

    private void ApplyEditorState(RecordedSessionEditorState state)
    {
        var previous = currentEditorState;
        currentEditorState = state;
        PreferencesPage.RefreshSignalDisplayPreferences(previous.Preferences.SignalDisplay, state.Preferences.SignalDisplay);
        signalRowActions.ApplyActionStates(state);
        if (state.Presentation.MapState.ReservesLayout && !mapInitializeRequested)
        {
            mapInitializeRequested = true;
            _ = mapViewModel?.InitializeAsync();
        }

        if (previous.Session != state.Session)
        {
            if (state.Session is { } snapshot)
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(snapshot.Timestamp ?? 0).LocalDateTime;
                if (previous.Session?.VelocityFilterWindowMilliseconds != snapshot.VelocityFilterWindowMilliseconds)
                    PreferencesPage.ApplyProcessingOptions(snapshot.ProcessingOptions);
            }

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
            OnPropertyChanged(nameof(SessionAnalysisRangeText));
        }
        if (previous.AnalysisSelection != state.AnalysisSelection)
        {
            OnPropertyChanged(nameof(ActiveFrontAnalysisSelection));
            OnPropertyChanged(nameof(ActiveRearAnalysisSelection));
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

        if (previous.Preferences.SignalDisplay != state.Preferences.SignalDisplay)
        {
            OnPropertyChanged(nameof(SignalDisplayPreferences));
        }

        if (previous.Preferences.SignalLayout != state.Preferences.SignalLayout)
        {
            OnPropertyChanged(nameof(SignalLayoutPreferences));
        }

        if (previous.Preferences.Layout != state.Preferences.Layout)
        {
            OnPropertyChanged(nameof(LayoutPreferences));
            OnPropertyChanged(nameof(MediaLayoutPreferences));
        }

        var travelDistributionChanged =
            previous.Preferences.Analysis.TravelDistributionMode != state.Preferences.Analysis.TravelDistributionMode;
        var velocityAverageChanged =
            previous.Preferences.Analysis.VelocityAverageMode != state.Preferences.Analysis.VelocityAverageMode;
        var balanceDisplacementChanged =
            previous.Preferences.Analysis.BalanceDisplacementMode != state.Preferences.Analysis.BalanceDisplacementMode;
        var balanceSpeedChanged =
            previous.Preferences.Analysis.BalanceSpeedMode != state.Preferences.Analysis.BalanceSpeedMode;

        if (travelDistributionChanged ||
            velocityAverageChanged ||
            balanceDisplacementChanged ||
            balanceSpeedChanged)
        {
            OnPropertyChanged(nameof(SessionAnalysisModesText));
        }
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        PublishEditorInput(pageCountInput, Pages.Count);
        editorActions.RefreshSelectedPageAnalysis();
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

    internal void SetSessionInsights(SessionInsightsResult insights)
    {
        PublishEditorInput(sessionInsightsInput, insights);
    }

    private void EvaluateDirtinessFromPageChange()
    {
        if (lifecycle == EditorLifecycle.ApplyingDraft)
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
        var result = await sessionCoordinator.LoadPreferencesAsync(Id);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLoaded) return;
        switch (result)
        {
            case SessionPreferenceLoadResult.Loaded loaded:
                ApplyRecordedPreferences(loaded.Preferences);
                break;
            case SessionPreferenceLoadResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                ApplyRecordedPreferences(SessionPreferences.Default);
                break;
        }
    }

    private void ApplyRecordedPreferences(SessionPreferences preferences)
    {
        analysisRequestScheduler.BeginBatch(suppressInsights: true);
        suppressInsightsRecompute = true;
        try
        {
            PublishEditorInput(preferenceReplayInput, preferences);
        }
        finally
        {
            suppressInsightsRecompute = false;
            analysisRequestScheduler.EndBatch();
        }
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

        if (effect is RecordedSessionEditorEffect.UpdateDirtyBaseline)
        {
            EvaluateDirtiness();
        }
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

        pendingPreferenceOperations = Task.WhenAll(pendingPreferenceOperations, PersistPreferenceChangeAsync(update));
    }

    private async Task PersistPreferenceChangeAsync(Func<SessionPreferences, SessionPreferences> update)
    {
        var result = await sessionCoordinator.SavePreferencesAsync(Id, update);
        if (IsLoaded && result is SessionPreferenceSaveResult.Failed failed)
            ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
    }

    private void OnProcessingPreferenceChangeCommitted(object? sender, EventArgs args) =>
        pendingPreferenceOperations = Task.WhenAll(pendingPreferenceOperations, processingPreferenceWorkflow.HandleProcessingPreferenceChangeCommittedAsync());

    #endregion Private methods

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness() => IsDirty = CaptureDraft() != draftBase;

    private SessionDraft CaptureDraft() => new(
        Id, Name ?? $"session #{Id}", NotesPage.Description ?? $"session #{Id}", draftBase.SetupId,
        NotesPage.ForkSettings.SpringRate, NotesPage.ForkSettings.HighSpeedCompression,
        NotesPage.ForkSettings.LowSpeedCompression, NotesPage.ForkSettings.LowSpeedRebound,
        NotesPage.ForkSettings.HighSpeedRebound, NotesPage.ShockSettings.SpringRate,
        NotesPage.ShockSettings.HighSpeedCompression, NotesPage.ShockSettings.LowSpeedCompression,
        NotesPage.ShockSettings.LowSpeedRebound, NotesPage.ShockSettings.HighSpeedRebound);

    protected override async Task SaveImplementation()
    {
        var result = await sessionCoordinator.SaveAsync(CaptureDraft(), draftBase);
        switch (result)
        {
            case SessionSaveResult.Saved saved:
                draftBase = SessionDraft.From(saved.Snapshot);
                EvaluateDirtiness();
                if (!IsDirty) shell.CompleteEdit(new EditorKey.Session(saved.Snapshot.Id));
                break;
            case SessionSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Session changed elsewhere",
                    "This session has been updated from another source. Discard your changes and reload?");
                if (reload) await ApplyPersistedSnapshotAsync(conflict.CurrentSnapshot);
                break;
            case SessionSaveResult.Missing:
                Notifications.Add("The session is no longer available.");
                break;
            case SessionSaveResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    protected override Task ResetImplementation()
    {
        var previousLifecycle = lifecycle;
        lifecycle = EditorLifecycle.ApplyingDraft;
        try
        {
            Id = draftBase.Id;
            Name = draftBase.Name;

            NotesPage.Description = draftBase.Description;
            NotesPage.ForkSettings.SpringRate = draftBase.FrontSpringRate;
            NotesPage.ForkSettings.HighSpeedCompression = draftBase.FrontHighSpeedCompression;
            NotesPage.ForkSettings.LowSpeedCompression = draftBase.FrontLowSpeedCompression;
            NotesPage.ForkSettings.LowSpeedRebound = draftBase.FrontLowSpeedRebound;
            NotesPage.ForkSettings.HighSpeedRebound = draftBase.FrontHighSpeedRebound;

            NotesPage.ShockSettings.SpringRate = draftBase.RearSpringRate;
            NotesPage.ShockSettings.HighSpeedCompression = draftBase.RearHighSpeedCompression;
            NotesPage.ShockSettings.LowSpeedCompression = draftBase.RearLowSpeedCompression;
            NotesPage.ShockSettings.LowSpeedRebound = draftBase.RearLowSpeedRebound;
            NotesPage.ShockSettings.HighSpeedRebound = draftBase.RearHighSpeedRebound;
        }
        finally
        {
            lifecycle = previousLifecycle;
        }

        EvaluateDirtiness();

        return Task.CompletedTask;
    }

    protected override Task CloseImplementation() => closeTask ??= CloseCoreAsync();

    private async Task CloseCoreAsync()
    {
        StopLoadedSession();
        await Task.WhenAll(pendingDomainReactions, pendingPreferenceOperations);
        UnsubscribeVmEventHandlers();
        DisposeWorkspace(MobileWorkspace);
        DisposeWorkspace(SignalsWorkspace);
        DisposeWorkspace(MediaWorkspace);
        videoPane?.Dispose();
        segmentPicker.Dispose();
        segmentComparison.Dispose();
        DisposeWorkspace(AnalysisWorkspace);
        editorEffects.Dispose();
        editorStateSubscription.Dispose();
        editorStateController.Dispose();
        editorActions.Dispose();
        viewLoadOperation.Dispose();
        loadOperation.Dispose();
        analysisResultSubscription.Dispose();
        analysisResultState.Dispose();
        MapViewModel?.Dispose();
        editorInputSubjects.Dispose();
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
        NotesPage.ForkSettings.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
        NotesPage.ShockSettings.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
        NotesPage.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
    }

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        var result = await sessionCoordinator.DeleteAsync(Id);
        switch (result)
        {
            case SessionDeleteResult.Deleted:
                await shell.CloseEditorAsync(new EditorKey.Session(Id));
                break;
            case SessionDeleteResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
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

    [RelayCommand]
    private async Task Loaded()
    {
        if (lifecycle == EditorLifecycle.Closed) return;
        var wasLoaded = IsLoaded;
        lifecycle = EditorLifecycle.Loaded;
        var loadLifecycleToken = wasLoaded
            ? CancellationToken.None
            : viewLoadOperation.Start();
        processedTelemetryRetention ??= processedTelemetryReader.Retain(Id);
        if (wasLoaded)
        {
            return;
        }

        // Subscribe before the awaited restore so a remote sync apply that
        // lands while restore is in flight is not missed.
        var watch = recordedSessionProjection.WatchSession(Id).ObserveOnUiThread(UiThreadDispatcher);
        var operationWatch = sessionOperationRunner.Watch(Id).ObserveOnUiThread(UiThreadDispatcher);
        EnsureScopedSubscription(s =>
        {
            s.Add(sessionPreferences.ObserveRecorded(Id).ObserveOnUiThread(UiThreadDispatcher).Subscribe(OnPreferencesArrived));
            s.Add(watch.Subscribe(snapshot =>
            {
                PublishEditorInput(domainInput, snapshot);
                pendingDomainReactions = Task.WhenAll(pendingDomainReactions, OnDomainChangedAsync(snapshot));
            }));
            s.Add(operationWatch.Subscribe(state => PublishEditorInput(sessionOperationStateInput, state)));
        });

        try
        {
            if (videoPane is not null)
            {
                await videoPane.RefreshClipsAsync();
            }

            await RestoreRecordedPreferencesAsync(loadLifecycleToken);
            if (!IsLoaded || loadLifecycleToken.IsCancellationRequested)
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
        if (!IsLoaded)
        {
            return;
        }

        pendingDomainReactions = Task.WhenAll(pendingDomainReactions, HandleDeferredDomainAsync());
    }

    private bool ShouldDeferDomainHandling() =>
        deferDomainHandlingWhenInactive &&
        !IsTabActive;

    private void OnPreferencesArrived(SessionPreferences preferences)
    {
        if (IsLoaded) ApplyRecordedPreferences(preferences);
    }

    private void StopLoadedSession()
    {
        lifecycle = EditorLifecycle.Closed;
        viewLoadOperation.Cancel();
        loadOperation.Cancel();
        handledDomain = null;
        deferredDomain = null;
        sessionOperationRunner.Cancel(Id);
        segmentComparison.Clear();
        DisposeProcessedTelemetryRetention();
        DisposeScopedSubscriptions();
    }

    #endregion
}
