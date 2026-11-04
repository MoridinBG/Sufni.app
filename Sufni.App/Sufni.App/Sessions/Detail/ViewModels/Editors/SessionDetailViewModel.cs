using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Services.Preferences;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Plots;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels.Base;
using Sufni.App.Video.Models;
using Sufni.App.Video.ViewModels;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>
/// Composes a recorded session's detail tab and its editor-scoped presentation parts.
/// Owns the authored draft, domain reactions, editor commands and tab lifetime.
/// </summary>
public sealed partial class SessionDetailViewModel : TabPageViewModelBase
{
    public Guid Id { get; private set; }

    public ISessionShellMobileWorkspace MobileWorkspace { get; }
    public IRecordedSessionSignalsWorkspace SignalsWorkspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }
    public ISessionAnalysisWorkspace AnalysisWorkspace { get; }
    public ISessionSidebarWorkspace SidebarWorkspace { get; }

    private readonly CancellationTokenSource editorLifetime = new();
    private readonly TimelineAlignmentController alignmentController;
    private readonly SessionWindowEditController windowEditController;
    private readonly SignalPlotContextActions plotContextActions;
    private readonly RecordedSessionLoadController loadController;
    private readonly SessionStalenessPrompt stalenessPrompt;
    private readonly RecordedSessionPreferencesController preferencesController;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IRecordedSessionProjection recordedSessionProjection;
    private readonly ObservableCollection<PageViewModelBase> pages = [];
    private readonly SessionTimelineLinkViewModel timeline = new();
    private readonly VideoPaneViewModel? videoPane;
    private readonly SegmentPickerViewModel segmentPicker;
    private readonly SegmentComparisonViewModel segmentComparison;
    private readonly TelemetrySourceVisibilityStore sourceVisibility = new();
    private readonly ISessionOperationRunner sessionOperationRunner;
    private static readonly ILogger logger = Log.ForContext<SessionDetailViewModel>();
    private Task pendingDomainReactions = Task.CompletedTask;
    private readonly RecordedSessionAnalysisDemand analysisDemand;
    private readonly RecordedSessionEditorActions editorActions = new();
    private readonly IDisposable editorStateSubscription;
    private readonly Subject<int> pageCountInput = new();
    private readonly Subject<SessionPreferences> preferenceReplayInput = new();
    private readonly Subject<RecordedSessionLoadPresentation> loadPresentationInput = new();
    private readonly Subject<SessionOperationPresentationState> sessionOperationStateInput = new();
    private readonly Subject<SessionDampingPercentages> dampingPercentagesInput = new();
    private readonly Subject<SessionInsightsResult> sessionInsightsInput = new();
    private readonly Subject<AnalysisAvailabilityResult> analysisAvailabilityInput = new();
    private readonly Subject<SelectionHighlightsAnalysisResult> selectionHighlightsInput = new();
    private readonly Subject<RecordedSessionDomainSnapshot> domainInput = new();
    private readonly CompositeDisposable editorInputSubjects;
    private readonly RecordedSessionEditorStateController editorStateController;
    private RecordedSessionEditorState currentEditorState = RecordedSessionEditorState.CreateInitial();
    private RecordedSessionDomainSnapshot? handledDomain;
    private RecordedSessionDomainSnapshot? deferredDomain;
    private readonly RecordedPagePresentationApplier presentationApplier;
    private SessionDraft draftBase;
    private SessionInsightsPageViewModel AnalysisPage { get; }

    private readonly CancellableOperation viewLoadOperation = new();
    private enum EditorLifecycle { Created, Loaded, ApplyingDraft, Closed }
    private EditorLifecycle lifecycle;
    private bool IsLoaded => lifecycle == EditorLifecycle.Loaded;
    private Task? closeTask;
    private readonly bool deferDomainHandlingWhenInactive;
    private MapViewModel? mapViewModel;

    private NotesPageViewModel NotesPage { get; } = new();
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

    private PreferencesPageViewModel PreferencesPage { get; }

    public SessionScreenPresentationState ScreenState => currentEditorState.Presentation.ScreenState;
    public SessionOperationPresentationState SessionOperationState => currentEditorState.Presentation.OperationState;

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
            if (IsLoaded) ReportError(FailurePresentation.From(SessionEditorFailureReason.Reconcile));
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

        if (!editorLifetime.IsCancellationRequested) await stalenessPrompt.ReconcileStalenessAsync(domain);
    }

    private async Task ApplyDomainReactionAsync(RecordedSessionDomainSnapshot previous, RecordedSessionDomainSnapshot current)
    {
        var snapshot = current.Session;
        var metadataChanged = RequiresDraftReconciliation(previous.Session, snapshot);
        var reloadTelemetry = RequiresTelemetryReload(previous, current);

        if (metadataChanged && IsDirty)
        {
            // Refresh derived telemetry even while the discard prompt is pending so
            // plots never show stale data; unsaved edits are preserved.
            if (reloadTelemetry)
            {
                await loadController.RequestLoadAsync();
            }

            if (editorLifetime.IsCancellationRequested) return;
            var discard = await dialogService.ShowConfirmationAsync(
                "Session changed elsewhere",
                "This session has been updated from another source. Discard your changes and reload?");
            if (editorLifetime.IsCancellationRequested) return;
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
                await loadController.RequestLoadAsync();
            }

            return;
        }

        if (reloadTelemetry)
        {
            await loadController.RequestLoadAsync();
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

    private void ReportError(FailurePresentation failure)
    {
        if (lifecycle != EditorLifecycle.Closed) ErrorMessages.Add(failure);
    }

    private void Notify(string message)
    {
        if (lifecycle != EditorLifecycle.Closed) Notifications.Add(message);
    }

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
        IBikeCoordinator bikeCoordinator,
        IVideoPaneFactory? videoPaneFactory = null,
        TrackSnapshot? initialTrack = null)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sessionPreferences);
        this.deferDomainHandlingWhenInactive = deferDomainHandlingWhenInactive;

        this.sessionCoordinator = sessionCoordinator;
        this.sessionOperationRunner = sessionOperationRunner;
        this.recordedSessionProjection = recordedSessionProjection;
        editorInputSubjects = new CompositeDisposable(pageCountInput, preferenceReplayInput, loadPresentationInput,
            sessionOperationStateInput, dampingPercentagesInput, sessionInsightsInput, analysisAvailabilityInput,
            selectionHighlightsInput, domainInput);
        analysisDemand = new RecordedSessionAnalysisDemand(editorActions.Intents,
            analysisResultStateFactory, IsInsightsPage,
            value => PublishEditorInput(analysisAvailabilityInput, value),
            value => PublishEditorInput(selectionHighlightsInput, value),
            value => PublishEditorInput(dampingPercentagesInput, value),
            value => PublishEditorInput(sessionInsightsInput, value), ReportError, editorLifetime.Token);
        editorStateController = new RecordedSessionEditorStateController(
            new RecordedSessionEditorStateInputs(editorActions.Intents, pageCountInput, preferenceReplayInput,
                loadPresentationInput, sessionOperationStateInput, dampingPercentagesInput, sessionInsightsInput,
                analysisAvailabilityInput, selectionHighlightsInput, domainInput), analysisDemand);
        PreferencesPage = new PreferencesPageViewModel(() => editorStateController.Current.Preferences.SignalDisplay,
            editorActions.SetSignalDisplayPreferences, () => editorStateController.Current.ProcessingOptions,
            editorActions.PreviewProcessingOption, editorActions.CommitProcessingOption);
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
            (start, end) => { if (!editorLifetime.IsCancellationRequested) timeline.SetVisibleRange(start, end, this); },
            ReportError,
            Notify);
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
                if (editorLifetime.IsCancellationRequested) return;
                if (range is { } value)
                {
                    editorActions.SetAnalysisRange(value);
                }
                else
                {
                    editorActions.ClearAnalysisRange();
                }
            },
            ReportError,
            Notify);
        MobileWorkspace = new SessionShellMobileWorkspaceViewModel(this, pages, editorStateController.State, editorActions);
        videoPane = videoPaneFactory?.Create(
            timeline,
            editorStateController.State.Select(state => new VideoSessionTiming(
                state.Domain?.Source?.SourceHash,
                state.Session?.WindowStartSeconds ?? 0,
                state.TelemetryData?.Metadata.Duration ?? state.Session?.DurationSeconds)));
        alignmentController = new TimelineAlignmentController(Id, editorStateController.State,
            sessionCoordinator, videoPane, editorLifetime.Token, ReportError, Notify);
        windowEditController = new SessionWindowEditController(Id, editorStateController.State,
            sessionEditingCoordinator, shell, uiThreadDispatcher, editorLifetime.Token, ReportError, Notify);
        plotContextActions = new SignalPlotContextActions(editorStateController.State, editorActions,
            timeline, segmentPicker, alignmentController, windowEditController, editorLifetime.Token);
        SignalsWorkspace = new RecordedSessionSignalsWorkspaceViewModel(
            editorStateController.State, analysisDemand.ResultState, sourceVisibility, timeline,
            editorActions, segmentPicker, segmentComparison, plotContextActions);
        mapViewModel = mapViewModelFactory.Create();
        MediaWorkspace = new SessionMediaWorkspaceViewModel(
            editorStateController.State, mapViewModel, timeline, videoPane, segmentComparison);
        var dampingCutoffWorkflow = new DampingCutoffWorkflow(
            () => currentEditorState.Domain?.Bike,
            () => currentEditorState.Intent.DampingSpeedCutoffs,
            editorActions.SetDampingSpeedCutoffs,
            bikeCoordinator,
            ReportError, editorLifetime.Token);
        var analysisWorkspace = new SessionAnalysisWorkspaceViewModel(
            editorStateController.State, dampingCutoffWorkflow, analysisDemand, editorActions, segmentComparison);
        AnalysisWorkspace = analysisWorkspace;
        var positionVelocityPage = analysisWorkspace.PositionVelocity;
        SidebarWorkspace = new SessionSidebarWorkspaceViewModel(
            this, () => Name, value => Name = value, NotesPage, PreferencesPage, SaveCommand, ResetCommand);
        var signalsPage = new RecordedSignalsPageViewModel(SignalsWorkspace, MediaWorkspace);
        var springPage = new SpringPageViewModel(AnalysisWorkspace);
        var strokesPage = new StrokesPageViewModel(AnalysisWorkspace);
        var dampingPage = new DampingPageViewModel(AnalysisWorkspace);
        var balancePage = new BalancePageViewModel(AnalysisWorkspace);
        var vibrationPage = new VibrationPageViewModel(AnalysisWorkspace);
        AnalysisPage = new SessionInsightsPageViewModel(AnalysisWorkspace);
        pages.Add(signalsPage);
        pages.Add(springPage);
        pages.Add(strokesPage);
        pages.Add(dampingPage);
        pages.Add(positionVelocityPage);
        pages.Add(balancePage);
        pages.Add(vibrationPage);
        pages.Add(AnalysisPage);
        pages.Add(NotesPage);
        pages.Add(PreferencesPage);
        presentationApplier = new RecordedPagePresentationApplier(pages, springPage, dampingPage, balancePage,
            vibrationPage, AnalysisPage, NotesPage, PreferencesPage, editorStateController.State);
        loadController = new RecordedSessionLoadController(Id, editorStateController.State, sessionLoader,
            sessionStore, processedTelemetryReader, uiThreadDispatcher,
            editorLifetime.Token, value => PublishEditorInput(loadPresentationInput, value), ReportError);
        stalenessPrompt = new SessionStalenessPrompt(editorStateController.State, sessionCoordinator,
            dialogService, uiThreadDispatcher, () => IsDirty, editorLifetime.Token, ReportError, Notify);
        preferencesController = new RecordedSessionPreferencesController(Id, editorStateController.State, editorActions, sessionStore,
            sessionPreferences, sessionCoordinator,
            editorLifetime.Token, value => PublishEditorInput(preferenceReplayInput, value), ReportError, Notify);
        pages.CollectionChanged += OnPagesChanged;
        PublishEditorInput(pageCountInput, pages.Count);
        if (snapshot.HasProcessedData)
        {
            PublishEditorInput(loadPresentationInput, new RecordedSessionLoadPresentation.Loading(
                initialTrack is not null,
                SessionDetailLoadProgress.PreparingSession,
                snapshot));
        }

        NotesPage.ForkSettings.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        NotesPage.ShockSettings.PropertyChanged += OnNotesPageDirtinessPropertyChanged;
        NotesPage.PropertyChanged += OnNotesPageDirtinessPropertyChanged;

        ResetImplementation();
        if (!snapshot.HasProcessedData)
        {
            PublishEditorInput(loadPresentationInput, new RecordedSessionLoadPresentation.Empty(snapshot));
        }

    }

    private void ApplyEditorState(RecordedSessionEditorState state)
    {
        var previous = currentEditorState;
        currentEditorState = state;
        if (previous.Session != state.Session && state.Session is { } snapshot)
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(snapshot.Timestamp ?? 0).LocalDateTime;

        if (previous.Presentation.ScreenState != state.Presentation.ScreenState)
        {
            OnPropertyChanged(nameof(ScreenState));
        }

        if (previous.Presentation.OperationState != state.Presentation.OperationState)
        {
            OnPropertyChanged(nameof(SessionOperationState));
        }

        if (previous.Preferences.Layout != state.Preferences.Layout)
        {
            OnPropertyChanged(nameof(LayoutPreferences));
            OnPropertyChanged(nameof(MediaLayoutPreferences));
        }

    }

    private bool IsInsightsPage(int index) => pages.Count > 0 && AnalysisPage is not null &&
        ReferenceEquals(pages[RecordedSessionPageSelection.ClampSelectedPageIndex(index, pages.Count)], AnalysisPage);

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        PublishEditorInput(pageCountInput, pages.Count);
        analysisDemand.OnPagesChanged();
    }

    private void EvaluateDirtinessFromPageChange()
    {
        if (lifecycle == EditorLifecycle.ApplyingDraft)
        {
            return;
        }

        EvaluateDirtiness();
    }

    private void OnNotesPageDirtinessPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        EvaluateDirtinessFromPageChange();
    }

    private static bool RequiresDraftReconciliation(SessionSnapshot previous, SessionSnapshot current) =>
        SessionDraft.From(previous) != SessionDraft.From(current);

    private static bool RequiresTelemetryReload(RecordedSessionDomainSnapshot previous, RecordedSessionDomainSnapshot current) =>
        previous.Session.HasProcessedData != current.Session.HasProcessedData ||
        previous.Session.ProcessedTelemetryRevision != current.Session.ProcessedTelemetryRevision ||
        previous.Session.ProcessingFingerprintJson != current.Session.ProcessingFingerprintJson ||
        previous.Session.DerivationWindow != current.Session.DerivationWindow ||
        previous.SelectedTrack != current.SelectedTrack ||
        previous.Session.GpsOffsetSeconds != current.Session.GpsOffsetSeconds ||
        previous.Session.Timestamp != current.Session.Timestamp ||
        previous.Session.DurationSeconds != current.Session.DurationSeconds;

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
        if (editorLifetime.IsCancellationRequested) return;
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
                if (editorLifetime.IsCancellationRequested) return;
                if (reload) await ApplyPersistedSnapshotAsync(conflict.CurrentSnapshot);
                break;
            case SessionSaveResult.Missing:
                Notify("The session is no longer available.");
                break;
            case SessionSaveResult.Failed failed:
                ReportError(FailurePresentation.From(failed.Reason, failed.Details));
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
        analysisDemand.Stop();
        await Task.WhenAll(pendingDomainReactions, preferencesController.PendingOperations);
        UnsubscribeVmEventHandlers();
        DisposeWorkspace(MobileWorkspace);
        DisposeWorkspace(SignalsWorkspace);
        DisposeWorkspace(MediaWorkspace);
        videoPane?.Dispose();
        segmentPicker.Dispose();
        await segmentComparison.DisposeAsync();
        DisposeWorkspace(AnalysisWorkspace);
        preferencesController.Dispose();
        stalenessPrompt.Dispose();
        alignmentController.Dispose();
        windowEditController.Dispose();
        plotContextActions.Dispose();
        presentationApplier.Dispose();
        editorStateSubscription.Dispose();
        editorStateController.Dispose();
        editorActions.Dispose();
        viewLoadOperation.Dispose();
        loadController.Dispose();
        await analysisDemand.DisposeAsync();
        mapViewModel?.Dispose();
        editorInputSubjects.Dispose();
        editorLifetime.Dispose();
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
        pages.CollectionChanged -= OnPagesChanged;
        NotesPage.ForkSettings.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
        NotesPage.ShockSettings.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
        NotesPage.PropertyChanged -= OnNotesPageDirtinessPropertyChanged;
    }

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        var result = await sessionCoordinator.DeleteAsync(Id);
        if (editorLifetime.IsCancellationRequested) return;
        switch (result)
        {
            case SessionDeleteResult.Deleted:
                await shell.CloseEditorAsync(new EditorKey.Session(Id));
                break;
            case SessionDeleteResult.Failed failed:
                ReportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    [RelayCommand]
    private async Task Loaded()
    {
        if (lifecycle == EditorLifecycle.Closed) return;
        var wasLoaded = IsLoaded;
        lifecycle = EditorLifecycle.Loaded;
        var loadLifecycleToken = wasLoaded
            ? CancellationToken.None
            : viewLoadOperation.Start(editorLifetime.Token);
        loadController.Start();
        if (wasLoaded)
        {
            return;
        }

        // Subscribe before the awaited restore so a remote sync apply that
        // lands while restore is in flight is not missed.
        var watch = recordedSessionProjection.WatchSession(Id).ObserveOnUiThread(UiThreadDispatcher);
        var operationWatch = sessionOperationRunner.Watch(Id).ObserveOnUiThread(UiThreadDispatcher);
        preferencesController.Start();
        EnsureScopedSubscription(s =>
        {
            s.Add(watch.Subscribe(snapshot =>
            {
                PublishEditorInput(domainInput, snapshot);
                pendingDomainReactions = Task.WhenAll(pendingDomainReactions, OnDomainChangedAsync(snapshot));
            }));
            s.Add(operationWatch.Subscribe(state => PublishEditorInput(sessionOperationStateInput, state)));
        });

        try
        {
            await preferencesController.RestoreAsync(loadLifecycleToken);
            if (loadLifecycleToken.IsCancellationRequested)
            {
                return;
            }

            await loadController.RequestLoadAsync();
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

    private void StopLoadedSession()
    {
        lifecycle = EditorLifecycle.Closed;
        editorLifetime.Cancel();
        viewLoadOperation.Cancel();
        loadController.Stop();
        handledDomain = null;
        deferredDomain = null;
        sessionOperationRunner.Cancel(Id);
        segmentComparison.Clear();
        preferencesController.Stop();
        DisposeScopedSubscriptions();
    }

}
