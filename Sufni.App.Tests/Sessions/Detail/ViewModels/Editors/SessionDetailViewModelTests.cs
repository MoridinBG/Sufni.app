using Sufni.App.Shared.Plots;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Shared.Views.Dialogs;
using Sufni.App.Shared.Models;
using Sufni.App.Video.Coordinators;
using Sufni.App.Shell.Coordinators;
using Avalonia.Threading;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using System.ComponentModel;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;
using Sufni.App.Video.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

// Includes Avalonia video doubles and dispatcher-driven editor updates.
[Collection("Ui")]
public class SessionDetailViewModelTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreferenceSaves_PreserveNewerEditsUntilTheirOwnOutcome(bool latestSaveSucceeds)
    {
        using var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor();
        await harness.LoadAsync(editor);
        // Inline completion keeps each persistence boundary under dispatcher control.
        var first = new TaskCompletionSource<SessionPreferenceSaveResult>();
        var second = new TaskCompletionSource<SessionPreferenceSaveResult>();
        harness.SessionCoordinator.SavePreferencesAsync(editor.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(first.Task, second.Task);
        var modes = new List<BalanceSpeedMode>();
        ((INotifyPropertyChanged)editor.AnalysisWorkspace).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ISessionAnalysisWorkspace.SelectedBalanceSpeedMode))
                modes.Add(editor.AnalysisWorkspace.SelectedBalanceSpeedMode);
        };

        editor.AnalysisWorkspace.SelectedBalanceSpeedMode = BalanceSpeedMode.LowSpeed;
        editor.AnalysisWorkspace.SelectedBalanceSpeedMode = BalanceSpeedMode.HighSpeed;
        var committed = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with { BalanceSpeedMode = BalanceSpeedMode.LowSpeed },
        };
        harness.PublishPreferences(committed);
        first.SetResult(new SessionPreferenceSaveResult.Saved());
        // An unrelated persisted edit still reaches the screen while the second save is pending.
        committed = committed with
        {
            SignalDisplay = committed.SignalDisplay with { TravelSmoothing = PlotSmoothingLevel.Strong },
        };
        harness.PublishPreferences(committed);

        Assert.Equal([BalanceSpeedMode.LowSpeed, BalanceSpeedMode.HighSpeed], modes);
        Assert.Equal(PlotSmoothingLevel.Strong, editor.SidebarWorkspace.PreferencesPage.TravelSignal.SelectedSmoothing);
        if (latestSaveSucceeds)
        {
            harness.PublishPreferences(committed with
            {
                Analysis = committed.Analysis with { BalanceSpeedMode = BalanceSpeedMode.HighSpeed },
            });
            second.SetResult(new SessionPreferenceSaveResult.Saved());
        }
        else
        {
            second.SetResult(new SessionPreferenceSaveResult.Failed(SessionEditorFailureReason.SavePreferences, null));
        }

        Assert.Equal(latestSaveSucceeds ? BalanceSpeedMode.HighSpeed : BalanceSpeedMode.LowSpeed,
            editor.AnalysisWorkspace.SelectedBalanceSpeedMode);
        Assert.Equal(PlotSmoothingLevel.Strong, editor.SidebarWorkspace.PreferencesPage.TravelSignal.SelectedSmoothing);
        Assert.Equal(latestSaveSucceeds ? 0 : 1, editor.ErrorMessages.Count);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task PreferenceRestore_DoesNotReplaceAnObservedChange()
    {
        using var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor();
        var pending = new TaskCompletionSource<SessionPreferenceLoadResult>();
        harness.SessionCoordinator.LoadPreferencesAsync(editor.Id).Returns(pending.Task);
        var loading = harness.LoadAsync(editor);
        harness.PublishPreferences(SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with { BalanceSpeedMode = BalanceSpeedMode.HighSpeed },
        });

        pending.SetResult(new SessionPreferenceLoadResult.Loaded(SessionPreferences.Default));
        await loading.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BalanceSpeedMode.HighSpeed, editor.AnalysisWorkspace.SelectedBalanceSpeedMode);
        await harness.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessingCompletion_PreservesAnEditMadeWhileSaving(bool succeeds)
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession() with { VelocityFilterWindowMilliseconds = 25 };
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: changes);
        await harness.LoadAsync(editor);
        var pending = new TaskCompletionSource<SessionRecomputeResult>();
        harness.SessionCoordinator.SetProcessingOptionAsync(snapshot.Id, 50).Returns(pending.Task);
        var preferences = editor.SidebarWorkspace.PreferencesPage;
        preferences.VelocityFilterWindowMilliseconds = 50;
        preferences.CommitProcessingPreferenceChange();
        preferences.VelocityFilterWindowMilliseconds = 75;

        var committed = snapshot with { VelocityFilterWindowMilliseconds = succeeds ? 50 : 25 };
        harness.SessionStore.Get(snapshot.Id).Returns(committed);
        changes.OnNext(DomainFromSnapshot(committed));
        pending.SetResult(succeeds ? new SessionRecomputeResult.Recomputed()
            : new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.ProcessingOption));

        Assert.Equal(75, preferences.VelocityFilterWindowMilliseconds);
        Assert.Equal(succeeds ? 0 : 1, editor.ErrorMessages.Count);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ProcessingFailure_RestoresCommittedOptionsAndAllowsRetry()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession() with { VelocityFilterWindowMilliseconds = 25 };
        var editor = harness.CreateRecordedEditor(snapshot);
        await harness.LoadAsync(editor);
        harness.SessionCoordinator.SetProcessingOptionAsync(snapshot.Id, 50)
            .Returns(new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.ProcessingOption));
        var preferences = editor.SidebarWorkspace.PreferencesPage;
        preferences.VelocityFilterWindowMilliseconds = 50;
        preferences.CommitProcessingPreferenceChange();
        Assert.Equal(25, preferences.VelocityFilterWindowMilliseconds);

        preferences.VelocityFilterWindowMilliseconds = 50;
        preferences.CommitProcessingPreferenceChange();

        await harness.SessionCoordinator.Received(2).SetProcessingOptionAsync(snapshot.Id, 50);
        Assert.Equal(25, preferences.VelocityFilterWindowMilliseconds);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task BalancePage_RemainsAvailableForAnEmptySelectedRange()
    {
        using var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor(loadResult: harness.CreateLoadedResult(TestTelemetryData.CreateProcessed(sampleRate: 100)));
        await harness.LoadAsync(editor);
        var page = Assert.Single(editor.MobileWorkspace.Pages.OfType<BalancePageViewModel>());

        editor.SignalsWorkspace.SetAnalysisRange(0, 0.1);

        Assert.Contains(page, editor.MobileWorkspace.Pages);
        Assert.Equal(SurfaceStateKind.NoData, page.CompressionPresentationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, page.ReboundPresentationState.Kind);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Close_DropsLateAlignmentFeedback()
    {
        using var videos = new VideoFixture();
        var (harness, editor, clip) = await CreateEditorWithVideoAsync(videos);
        using var harnessLifetime = harness;
        var pending = new TaskCompletionSource<VideoAlignmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        videos.Coordinator.ShiftAlignmentAsync(clip.Id, 2).Returns(pending.Task);
        ExecuteSignalAction(editor, "video-mark-video-event", 5);
        var operation = ((IAsyncRelayCommand)FindSignalAction(editor, "video-mark-telemetry-event").Command)
            .ExecuteAsync(CreatePlotContext(3));

        await harness.CloseAsync(editor);
        Assert.False(operation.IsCompleted);
        pending.SetResult(new VideoAlignmentResult.Failed(VideoAlignmentFailureReason.Save, "disk full"));
        await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(editor.ErrorMessages);
        Assert.Empty(editor.Notifications);
    }

    [AvaloniaFact]
    public async Task DampingCommit_AfterCloseDoesNotRestoreCutoffs()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var bike = TestSnapshots.Bike();
        var domain = DomainFromSnapshot(snapshot) with { Bike = bike };
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: Observable.Return(domain));
        await harness.LoadAsync(editor);
        var pending = new TaskCompletionSource<BikeDampingSpeedCutoffUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.BikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, 200, SuspensionType.Front, DampingSpeedCircuit.Compression, 350)
            .Returns(pending.Task);
        var commit = editor.AnalysisWorkspace.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 350);
        await harness.CloseAsync(editor);

        pending.SetResult(new BikeDampingSpeedCutoffUpdateResult.Missing());
        await commit.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(350, editor.AnalysisWorkspace.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Insights_FirstPageSelectionRequestsAnalysisInTheSameStateUpdate()
    {
        using var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor();
        await harness.LoadAsync(editor);
        harness.SessionInsightsService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());

        editor.MobileWorkspace.SelectedPageIndex = editor.MobileWorkspace.Pages.IndexOf(
            editor.MobileWorkspace.Pages.OfType<SessionInsightsPageViewModel>().Single());

        harness.SessionInsightsService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(
            request => request.TelemetryData == editor.SignalsWorkspace.TelemetryData && request.AnalysisRange == null));
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Insights_LeavingThePageRetainsDemandForChangedInputs()
    {
        using var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor();
        await harness.LoadAsync(editor);
        editor.MobileWorkspace.SelectedPageIndex = editor.MobileWorkspace.Pages.IndexOf(
            editor.MobileWorkspace.Pages.OfType<SessionInsightsPageViewModel>().Single());
        editor.MobileWorkspace.SelectedPageIndex = 0;
        harness.SessionInsightsService.ClearReceivedCalls();

        editor.SignalsWorkspace.SetAnalysisRange(0.05, 0.2);

        harness.SessionInsightsService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(
            request => request.AnalysisRange == new TelemetryTimeRange(0.05, 0.2)));
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Insights_ChangedInputsReplaceTheResultWhileDemandIsRetained()
    {
        using var harness = new SessionDetailHarness();
        var initialResult = CreateAnalysisResult();
        var changedResult = initialResult with { AllFindings = [] };
        harness.SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>())
            .Returns(call => call.Arg<SessionInsightsRequest>().AnalysisRange == null ? initialResult : changedResult);
        var editor = harness.CreateRecordedEditor();
        await harness.LoadAsync(editor);
        editor.MobileWorkspace.SelectedPageIndex = editor.MobileWorkspace.Pages.IndexOf(
            editor.MobileWorkspace.Pages.OfType<SessionInsightsPageViewModel>().Single());
        Assert.Same(initialResult, editor.AnalysisWorkspace.SessionInsights);

        editor.SignalsWorkspace.SetAnalysisRange(0.1, 0.2);

        harness.SessionInsightsService.Received(2).Analyze(Arg.Any<SessionInsightsRequest>());
        Assert.Same(changedResult, editor.AnalysisWorkspace.SessionInsights);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ProcessingCommit_ProjectsCurrentStoreOptions()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession() with { VelocityFilterWindowMilliseconds = 25 };
        var editor = harness.CreateRecordedEditor(snapshot);
        await harness.LoadAsync(editor);
        var preferences = editor.SidebarWorkspace.PreferencesPage;
        harness.SessionCoordinator.SetProcessingOptionAsync(snapshot.Id, 50).Returns(_ =>
        {
            harness.SessionStore.Get(snapshot.Id).Returns(snapshot with { VelocityFilterWindowMilliseconds = 100 });
            return new SessionRecomputeResult.Recomputed();
        });
        preferences.VelocityFilterWindowMilliseconds = 50;

        preferences.CommitProcessingPreferenceChange();

        Assert.Equal(100, preferences.VelocityFilterWindowMilliseconds);
        await harness.SessionCoordinator.Received(1).SetProcessingOptionAsync(snapshot.Id, 50);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ProcessingCommit_SupersededResultPreservesNewerProjectionAndDraft()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession() with { VelocityFilterWindowMilliseconds = 25 };
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: changes);
        await harness.LoadAsync(editor);
        var preferences = editor.SidebarWorkspace.PreferencesPage;
        // Complete on the dispatcher so the pending commit resumes deterministically inline.
        var pending = new TaskCompletionSource<SessionRecomputeResult>();
        harness.SessionCoordinator.SetProcessingOptionAsync(snapshot.Id, 50).Returns(pending.Task);
        preferences.VelocityFilterWindowMilliseconds = 50;
        preferences.CommitProcessingPreferenceChange();
        var newer = snapshot with { VelocityFilterWindowMilliseconds = 100 };
        harness.SessionStore.Get(snapshot.Id).Returns(newer);
        changes.OnNext(DomainFromSnapshot(newer));
        Assert.Equal(100, preferences.VelocityFilterWindowMilliseconds);
        preferences.VelocityFilterWindowMilliseconds = 150;

        pending.SetResult(new SessionRecomputeResult.Superseded());

        Assert.Equal(150, preferences.VelocityFilterWindowMilliseconds);
        Assert.Empty(editor.ErrorMessages);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task LoadFailure_ShowsSafeScreenStateAndSeparateDiagnostics()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var editor = harness.CreateRecordedEditor(snapshot);
        harness.SessionLoader.LoadDetailAsync(snapshot.Id, Arg.Any<IProgress<SessionDetailLoadProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new SessionDetailLoadResult.Failed(SessionLoadFailureReason.Read, "payload diagnostic"));

        var spring = editor.MobileWorkspace.Pages.OfType<SpringPageViewModel>().Single();
        Assert.Equal(SurfaceStateKind.Loading, spring.FrontPresentationState.Kind);
        await harness.LoadAsync(editor);

        Assert.Equal(SurfaceStateKind.Hidden, spring.FrontPresentationState.Kind);
        Assert.DoesNotContain(editor.MobileWorkspace.Pages, page => page is BalancePageViewModel);
        Assert.True(editor.ScreenState.IsError);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.DoesNotContain("payload diagnostic", editor.ScreenState.Message);
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal("payload diagnostic", failure.Details);
        Assert.Equal(editor.ScreenState.Message, failure.Summary);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task PreferenceReadFailure_ShowsDefaultsAndSeparateDiagnostics()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        harness.SessionCoordinator.LoadPreferencesAsync(snapshot.Id)
            .Returns(new SessionPreferenceLoadResult.Failed(SessionEditorFailureReason.LoadPreferences, "file inaccessible"));
        var editor = harness.CreateRecordedEditor(snapshot);

        await harness.LoadAsync(editor);

        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal("file inaccessible", failure.Details);
        Assert.DoesNotContain(failure.Details!, failure.Summary);
        Assert.Equal(SessionPreferences.Default.SignalDisplay.VelocitySmoothing, editor.SidebarWorkspace.PreferencesPage.VelocitySignal.SelectedSmoothing);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Close_DrainsPendingPreferenceWriteWithoutApplyingLateFailure()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var editor = harness.CreateRecordedEditor(snapshot);
        await harness.LoadAsync(editor);
        var pending = new TaskCompletionSource<SessionPreferenceSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.SessionCoordinator.SavePreferencesAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(pending.Task);
        editor.SidebarWorkspace.PreferencesPage.VelocitySignal.SelectedSmoothing = PlotSmoothingLevel.Strong;

        var close = harness.CloseAsync(editor);
        Assert.False(close.IsCompleted);
        pending.SetResult(new SessionPreferenceSaveResult.Failed(SessionEditorFailureReason.SavePreferences, "disk full"));
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Close_DrainsPendingReconciliationWithoutApplyingItsLateFeedback()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: changes);
        await harness.LoadAsync(editor);
        var pending = new TaskCompletionSource<SessionStalenessReconciliation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.SessionCoordinator.ReconcileStalenessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), SessionRecomputeChoice.Inspect)
            .Returns(_ =>
            {
                started.TrySetResult();
                return pending.Task;
            });
        changes.OnNext(DomainFromSnapshot(snapshot));
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var close = harness.CloseAsync(editor);
        Assert.False(close.IsCompleted);
        pending.SetResult(new SessionStalenessReconciliation.Unavailable(new SessionStaleness.MissingRawSource()));
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task RecomputePrompt_DeduplicatesTheSameInputsAndReturnsForChangedProcessingInputs()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: changes);
        await harness.LoadAsync(editor);
        var fingerprint = new ProcessingFingerprint(4, 1, Guid.NewGuid(), Guid.NewGuid(), 1, "dependency", "source", 25);
        harness.SessionCoordinator.ReconcileStalenessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), SessionRecomputeChoice.Inspect)
            .Returns(call => new SessionStalenessReconciliation.Required(call.Arg<RecordedSessionDomainSnapshot>().CurrentFingerprint, null));
        var firstPrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = 0;
        harness.DialogService.ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(_ =>
            {
                if (++prompts == 1) firstPrompt.TrySetResult();
                else secondPrompt.TrySetResult();
                return Task.FromResult<string?>("cancel");
            });
        var domain = DomainFromSnapshot(snapshot, staleness: new SessionStaleness.Stale()) with { CurrentFingerprint = fingerprint };
        changes.OnNext(domain);
        await firstPrompt.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        changes.OnNext(domain);
        changes.OnNext(domain with { CurrentFingerprint = fingerprint with { VelocityFilterWindowMilliseconds = 100 } });
        await secondPrompt.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await harness.CloseAsync(editor);

        Assert.Equal(2, prompts);
    }

    [AvaloniaFact]
    public async Task SelectedTrackChange_ReloadsContentWithoutDiscardingDirtyMetadata()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(updated: 5);
        using var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateMinimal(duration: 10);
        var freshTelemetry = TestTelemetryData.CreateMinimal(duration: 20);
        var editor = harness.CreateRecordedEditor(snapshot, harness.CreateLoadedResult(oldTelemetry), watch);
        await harness.LoadAsync(editor);
        watch.OnNext(DomainFromSnapshot(snapshot));
        editor.SidebarWorkspace.DescriptionText = "dirty draft";
        harness.ConfigureLoad(snapshot, harness.CreateLoadedResult(freshTelemetry));

        watch.OnNext(DomainFromSnapshot(snapshot) with
        {
            SelectedTrack = new Sufni.App.MapsAndTracks.Stores.TrackSnapshot(Guid.NewGuid(), 100, 120, 1)
        });
        await WaitForAsync(() => ReferenceEquals(editor.SignalsWorkspace.TelemetryData, freshTelemetry));

        await harness.DialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.Equal(20, editor.SignalsWorkspace.TelemetryData!.Metadata.Duration);
        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.SidebarWorkspace.DescriptionText);
        await harness.SaveAsync(editor);
        await harness.SessionCoordinator.Received(1).SaveAsync(Arg.Is<SessionDraft>(draft => draft.Description == "dirty draft"), SessionDraft.From(snapshot));
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task InactiveTab_DefersStalenessPromptUntilActivated()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: changes);
        harness.SessionCoordinator.ReconcileStalenessAsync(
            Arg.Is<RecordedSessionDomainSnapshot>(domain => domain.Staleness is SessionStaleness.Stale),
            SessionRecomputeChoice.Inspect).Returns(new SessionStalenessReconciliation.Required(null, null));
        var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.DialogService.ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(_ =>
            {
                prompted.TrySetResult();
                return Task.FromResult<string?>(null);
            });
        editor.SetTabActive(false);
        await harness.LoadAsync(editor);
        changes.OnNext(DomainFromSnapshot(snapshot, staleness: new SessionStaleness.Stale()));

        await harness.LoadAsync(editor);

        Assert.False(prompted.Task.IsCompleted);

        editor.SetTabActive(true);
        await prompted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await harness.CloseAsync(editor);
        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.ReleasedSessionIds);
    }

    [AvaloniaFact]
    public async Task Loaded_PublishesTelemetryBeforeTrackCompletes()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackPoints = new List<TrackPoint> { new(2, 2, 2, 0) };
        var pendingTrack = new TaskCompletionSource<SessionDetailTrackLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(telemetry));
        harness.SessionLoader.LoadTrackAsync(
                snapshot.Id,
                telemetry,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(pendingTrack.Task);

        var loadTask = harness.LoadAsync(editor);
        await WaitForAsync(() => ReferenceEquals(editor.SignalsWorkspace.TelemetryData, telemetry));

        Assert.False(loadTask.IsCompleted);
        Assert.Same(telemetry, editor.SignalsWorkspace.TelemetryData);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Null(editor.SignalsWorkspace.TrackPoints);

        pendingTrack.SetResult(new SessionDetailTrackLoadResult.Loaded(
            new SessionTrackPresentationData(
                Guid.NewGuid(),
                [new TrackPoint(1, 1, 1, 0)],
                trackPoints)));
        await loadTask;

        Assert.Equal(trackPoints, editor.SignalsWorkspace.TrackPoints);
        Assert.Same(telemetry, editor.SignalsWorkspace.TelemetryData);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Closed_DropsTrackCompletion_FromCanceledLoad()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var telemetry = TestTelemetryData.CreateProcessed();
        var pendingTrack = new TaskCompletionSource<SessionDetailTrackLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(telemetry));
        harness.SessionLoader.LoadTrackAsync(
                snapshot.Id,
                telemetry,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(pendingTrack.Task);

        var loadTask = harness.LoadAsync(editor);
        await WaitForAsync(() => ReferenceEquals(editor.SignalsWorkspace.TelemetryData, telemetry));

        await harness.CloseAsync(editor);
        pendingTrack.SetResult(new SessionDetailTrackLoadResult.Loaded(
            new SessionTrackPresentationData(
                Guid.NewGuid(),
                [new TrackPoint(1, 1, 1, 0)],
                [new TrackPoint(2, 2, 2, 0)])));
        await loadTask;

        Assert.Same(telemetry, editor.SignalsWorkspace.TelemetryData);
        Assert.Null(editor.SignalsWorkspace.TrackPoints);
    }

    [AvaloniaFact]
    public async Task TrackFailure_PreservesTelemetryAndShowsMapUnavailable()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var telemetry = TestTelemetryData.CreateProcessed();
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(telemetry));
        harness.SessionLoader.LoadTrackAsync(
                snapshot.Id,
                telemetry,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SessionDetailTrackLoadResult.Failed(SessionTrackFailureReason.Load, "track failed"));

        await harness.LoadAsync(editor);

        Assert.Same(telemetry, editor.SignalsWorkspace.TelemetryData);
        Assert.Null(editor.SignalsWorkspace.TrackPoints);
        Assert.Equal(SessionScreenStateKind.Ready, editor.ScreenState.Kind);
        Assert.Equal(SurfaceStateKind.Error, editor.MediaWorkspace.MapState.Kind);
        Assert.Equal("track failed", Assert.Single(editor.ErrorMessages).Details);
        Assert.DoesNotContain("track failed", editor.MediaWorkspace.MapState.Message);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.TravelSignalState.Kind);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Save_CompletesEdit_AfterAcceptingSavedDraft()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(updated: 5);
        var editor = harness.CreateRecordedEditor(snapshot);
        editor.Name = "renamed";
        harness.SessionCoordinator.SaveAsync(Arg.Any<SessionDraft>(), SessionDraft.From(snapshot))
            .Returns(new SessionSaveResult.Saved(snapshot with { Name = "renamed", Updated = 11 }));

        await harness.SaveAsync(editor);

        Assert.False(editor.IsDirty);
        harness.Shell.Received(1).CompleteEdit(new EditorKey.Session(snapshot.Id));
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Save_KeepsChangesMadeWhilePersistenceIsPendingDirty()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(name: "original");
        var editor = harness.CreateRecordedEditor(snapshot);
        editor.Name = "submitted";
        var pending = new TaskCompletionSource<SessionSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.SessionCoordinator.SaveAsync(Arg.Any<SessionDraft>(), SessionDraft.From(snapshot)).Returns(pending.Task);

        var save = harness.SaveAsync(editor);
        editor.Name = "newer draft";
        pending.SetResult(new SessionSaveResult.Saved(snapshot with { Name = "submitted" }));
        await save.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("newer draft", editor.Name);
        Assert.True(editor.IsDirty);
        harness.Shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(name: "old", updated: 5);
        var editor = harness.CreateRecordedEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Session(id: snapshot.Id, name: "remote-updated", updated: 12);
        harness.SessionCoordinator.SaveAsync(Arg.Any<SessionDraft>(), SessionDraft.From(snapshot))
            .Returns(new SessionSaveResult.Conflict(fresh));
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await harness.SaveAsync(editor);

        Assert.Equal("remote-updated", editor.Name);
        Assert.False(editor.IsDirty);
        harness.Shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        await harness.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_Unsuccessful_KeepsDraftAndDistinguishesMissingSession(bool missing)
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(updated: 5);
        var editor = harness.CreateRecordedEditor(snapshot);
        editor.Name = "renamed";
        harness.SessionCoordinator.SaveAsync(Arg.Any<SessionDraft>(), SessionDraft.From(snapshot))
            .Returns(missing ? new SessionSaveResult.Missing() : new SessionSaveResult.Failed(SessionSaveFailureReason.Save, "disk full"));

        await harness.SaveAsync(editor);

        Assert.True(editor.IsDirty);
        Assert.Equal("renamed", editor.Name);
        Assert.Equal(missing ? 0 : 1, editor.ErrorMessages.Count);
        Assert.Equal(missing ? 1 : 0, editor.Notifications.Count);
        harness.Shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
        await harness.CloseAsync(editor);
    }


    [AvaloniaFact]
    public async Task Closed_CancelsInFlightLoad_AndDropsResult()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var pending = new TaskCompletionSource<SessionDetailLoadResult>();
        var editor = harness.CreateRecordedEditor(
            snapshot,
            loadResult: IncompleteResult(snapshot.Id));
        harness.SessionLoader.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.Arg<CancellationToken>()));

        var loadTask = harness.LoadAsync(editor);
        await Task.Yield();

        await harness.CloseAsync(editor);
        pending.SetResult(harness.CreateLoadedResult(TestTelemetryData.CreateProcessed()));
        await loadTask;

        Assert.Null(editor.SignalsWorkspace.TelemetryData);
    }

    [AvaloniaFact]
    public async Task SupersededTrackCompletion_DoesNotReplaceLatestGeneration()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var firstTelemetry = TestTelemetryData.CreateProcessed();
        var latestTelemetry = TestTelemetryData.CreateMinimal(duration: 20);
        var staleTrack = new List<TrackPoint> { new(1, 1, 1, 0) };
        var latestTrack = new List<TrackPoint> { new(2, 2, 2, 0) };
        var pendingTrack = new TaskCompletionSource<SessionDetailTrackLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var loadCount = 0;
        var trackLoadCount = 0;
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(firstTelemetry),
            watch.AsObservable());
        harness.SessionLoader.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++loadCount switch
            {
                1 => Task.FromResult<SessionDetailLoadResult>(harness.CreateLoadedResult(firstTelemetry)),
                2 => Task.FromResult<SessionDetailLoadResult>(harness.CreateLoadedResult(latestTelemetry)),
                _ => throw new InvalidOperationException("Unexpected load request."),
            });
        harness.SessionLoader.LoadTrackAsync(
                snapshot.Id,
                Arg.Any<TelemetryData>(),
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++trackLoadCount switch
            {
                1 => pendingTrack.Task,
                2 => Task.FromResult<SessionDetailTrackLoadResult>(
                    new SessionDetailTrackLoadResult.Loaded(
                        new SessionTrackPresentationData(Guid.NewGuid(), null, latestTrack))),
                _ => throw new InvalidOperationException("Unexpected track load request."),
            });

        var firstLoadTask = harness.LoadAsync(editor);
        await WaitForAsync(() => trackLoadCount == 1);
        watch.OnNext(DomainFromSnapshot(snapshot));
        watch.OnNext(DomainFromSnapshot(
            snapshot with { Updated = 2, ProcessedTelemetryRevision = 2 }));
        await WaitForAsync(() => trackLoadCount == 2);
        await WaitForAsync(() => ReferenceEquals(editor.SignalsWorkspace.TelemetryData, latestTelemetry));
        await WaitForAsync(() => editor.SignalsWorkspace.TrackPoints?.SequenceEqual(latestTrack) == true);

        pendingTrack.SetResult(new SessionDetailTrackLoadResult.Loaded(
            new SessionTrackPresentationData(Guid.NewGuid(), null, staleTrack)));
        await firstLoadTask;

        Assert.Equal(20, editor.SignalsWorkspace.TelemetryData!.Metadata.Duration);
        Assert.Equal(latestTrack, editor.SignalsWorkspace.TrackPoints);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task SupersededLoadCompletion_DoesNotReplaceLatestTelemetry()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var firstRefresh = new TaskCompletionSource<SessionDetailLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestRefresh = new TaskCompletionSource<SessionDetailLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var initialTelemetry = TestTelemetryData.CreateMinimal(duration: 10);
        var staleTelemetry = TestTelemetryData.CreateMinimal(duration: 15);
        var latestTelemetry = TestTelemetryData.CreateMinimal(duration: 20);
        using var watch = new Subject<RecordedSessionDomainSnapshot>();
        var loadCount = 0;
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(initialTelemetry),
            watch.AsObservable());
        harness.SessionLoader.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++loadCount switch
            {
                1 => Task.FromResult<SessionDetailLoadResult>(harness.CreateLoadedResult(initialTelemetry)),
                2 => firstRefresh.Task,
                3 => latestRefresh.Task,
                _ => throw new InvalidOperationException("Unexpected load request."),
            });

        await harness.LoadAsync(editor);
        watch.OnNext(DomainFromSnapshot(snapshot));
        watch.OnNext(DomainFromSnapshot(
            snapshot with { Updated = 2, ProcessedTelemetryRevision = 2 }));
        await WaitForAsync(() => loadCount == 2);
        watch.OnNext(DomainFromSnapshot(
            snapshot with { Updated = 3, ProcessedTelemetryRevision = 3 }));
        await WaitForAsync(() => loadCount == 3);

        latestRefresh.SetResult(harness.CreateLoadedResult(latestTelemetry));
        await WaitForAsync(() => ReferenceEquals(editor.SignalsWorkspace.TelemetryData, latestTelemetry));
        firstRefresh.SetResult(harness.CreateLoadedResult(staleTelemetry));
        await harness.CloseAsync(editor);

        Assert.Equal(20, editor.SignalsWorkspace.TelemetryData!.Metadata.Duration);

    }

    [AvaloniaFact]
    public async Task Loaded_WhenLocalDataIncomplete_EntersIncompleteScreenState()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var editor = harness.CreateRecordedEditor(snapshot, IncompleteResult(snapshot.Id));

        await harness.LoadAsync(editor);

        Assert.Equal(SessionScreenStateKind.IncompleteLocalData, editor.ScreenState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.MediaWorkspace.MapState.Kind);
        Assert.Empty(editor.ErrorMessages);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task SessionOperationState_FollowsTheSessionOperation()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(TestTelemetryData.CreateProcessed()));
        await harness.LoadAsync(editor);

        var operation = harness.OperationRunner.Start(snapshot.Id, "Matching");
        operation.Report("Matching session 5/10", 50);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.SessionOperationState.IsVisible);
        Assert.Equal("Matching session 5/10", editor.SessionOperationState.Message);
        Assert.Equal(50, editor.SessionOperationState.Percent);

        operation.Dispose();
        Dispatcher.UIThread.RunJobs();

        Assert.False(editor.SessionOperationState.IsVisible);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task SignalPreferenceChange_PersistsWithoutDirtyingSessionMetadata()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var saved = SessionPreferences.Default;
        harness.SessionCoordinator.SavePreferencesAsync(
                snapshot.Id,
                Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(call =>
            {
                saved = call.Arg<Func<SessionPreferences, SessionPreferences>>()(saved);
                harness.PublishPreferences(saved);
                return new SessionPreferenceSaveResult.Saved();
            });
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(CreateVibrationTelemetry()));

        await harness.LoadAsync(editor);
        harness.SessionCoordinator.ClearReceivedCalls();

        editor.SidebarWorkspace.PreferencesPage.VelocitySignal.SelectedSmoothing = PlotSmoothingLevel.Strong;

        Assert.Equal(PlotSmoothingLevel.Strong, editor.SidebarWorkspace.PreferencesPage.VelocitySignal.SelectedSmoothing);
        Assert.False(editor.IsDirty);
        Assert.Equal(PlotSmoothingLevel.Strong, saved.SignalDisplay.VelocitySmoothing);
        await harness.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task RuntimeFreshUpdate_DeclinedDirtyReload_KeepsDraftAndBaseline()
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(
            name: "trail run",
            description: "persisted",
            hasProcessedData: true,
            updated: 5);
        var updatedSnapshot = snapshot with { Updated = 8, Description = "remote" };
        using var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateMinimal(duration: 10);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(oldTelemetry),
            watch.AsObservable());

        await harness.LoadAsync(editor);
        watch.OnNext(DomainFromSnapshot(snapshot));
        editor.SidebarWorkspace.DescriptionText = "dirty draft";

        watch.OnNext(DomainFromSnapshot(updatedSnapshot));
        await Task.Yield();

        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.SidebarWorkspace.DescriptionText);
        await harness.SaveAsync(editor);
        await harness.SessionCoordinator.Received(1).SaveAsync(Arg.Is<SessionDraft>(draft => draft.Description == "dirty draft"), SessionDraft.From(snapshot));
        Assert.Equal(10, editor.SignalsWorkspace.TelemetryData!.Metadata.Duration);
        await harness.DialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
        await harness.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataAndTelemetryChanges_RefreshTelemetry_AndKeepDirtyConflict(bool deferred)
    {
        using var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(
            name: "trail run",
            description: "persisted",
            hasProcessedData: true,
            updated: 5);
        var metadataSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var combinedSnapshot = metadataSnapshot with { ProcessedTelemetryRevision = snapshot.ProcessedTelemetryRevision + 1 };
        using var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateMinimal(duration: 10);
        var freshTelemetry = TestTelemetryData.CreateMinimal(duration: 20);
        var loadCount = 0;
        var editor = harness.CreateRecordedEditor(
            snapshot,
            harness.CreateLoadedResult(oldTelemetry),
            watch.AsObservable());
        harness.SessionLoader.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadCount++;
                return loadCount == 1
                    ? harness.CreateLoadedResult(oldTelemetry)
                    : harness.CreateLoadedResult(freshTelemetry);
            });
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        await harness.LoadAsync(editor);
        watch.OnNext(DomainFromSnapshot(snapshot));
        editor.SidebarWorkspace.DescriptionText = "dirty draft";

        if (deferred)
        {
            editor.SetTabActive(false);
            watch.OnNext(DomainFromSnapshot(metadataSnapshot));
        }
        watch.OnNext(DomainFromSnapshot(combinedSnapshot));
        if (deferred)
        {
            await harness.DialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
            editor.SetTabActive(true);
        }

        await WaitForAsync(() => ReferenceEquals(editor.SignalsWorkspace.TelemetryData, freshTelemetry));

        Assert.Equal(20, editor.SignalsWorkspace.TelemetryData!.Metadata.Duration);
        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.SidebarWorkspace.DescriptionText);
        await harness.SaveAsync(editor);
        await harness.SessionCoordinator.Received(1).SaveAsync(Arg.Is<SessionDraft>(draft => draft.Description == "dirty draft"), SessionDraft.From(snapshot));
        await harness.CloseAsync(editor);
    }





    [AvaloniaFact]
    public async Task Split_OpensBackgroundTabBeforeRecomputeCompletes()
    {
        var (harness, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        using var harnessLifetime = harness;
        var createdId = Guid.NewGuid();
        var completion = new TaskCompletionSource<SessionEditResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.SessionEditingCoordinator.SplitAsync(editor.Id, 3, Arg.Any<IProgress<SessionEditProgress>>())
            .Returns(call =>
            {
                call.Arg<IProgress<SessionEditProgress>>().Report(new SessionEditProgress(createdId));
                return completion.Task;
            });

        var operation = ((IAsyncRelayCommand)FindSignalAction(editor, "session-split-here").Command)
            .ExecuteAsync(CreatePlotContext(clickSeconds: 3));

        harness.Shell.Received(1).OpenEditor(new EditorKey.Session(createdId), background: true);
        Assert.False(operation.IsCompleted);
        completion.SetResult(new SessionEditResult.Applied());
        await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await harness.SessionEditingCoordinator.Received(1).SplitAsync(editor.Id, 3, Arg.Any<IProgress<SessionEditProgress>>());
        Assert.Single(editor.Notifications);
        await harness.CloseAsync(editor);
    }






    [AvaloniaFact]
    public async Task SegmentPicker_CanOpen_OnceTheTelemetryIsLoaded()
    {
        using var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor(TestSnapshots.Session(hasProcessedData: true));
        var canOpenBeforeLoad = editor.SignalsWorkspace.SegmentPicker.CanOpen;

        await harness.LoadAsync(editor);

        Assert.False(canOpenBeforeLoad);
        Assert.True(editor.SignalsWorkspace.SegmentPicker.CanOpen);
        await harness.CloseAsync(editor);
    }

    private static async Task<(SessionDetailHarness Harness, SessionDetailViewModel Editor)> CreateLoadedEditorAsync(
        SessionSnapshot snapshot)
    {
        var harness = new SessionDetailHarness();
        var editor = harness.CreateRecordedEditor(snapshot);
        await harness.LoadAsync(editor);
        return (harness, editor);
    }

    private static async Task<(SessionDetailHarness Harness, SessionDetailViewModel Editor, SessionVideoSnapshot Clip)> CreateEditorWithVideoAsync(
        VideoFixture videos)
    {
        var harness = new SessionDetailHarness();
        var snapshot = TestSnapshots.Session(timestamp: 1_000, hasProcessedData: true);
        var clip = videos.CreateClip("source-hash", startTimestamp: 1_000, durationSeconds: 60);
        harness.VideoPaneFactory = new VideoPaneFactory(
            await videos.CreateStoreAsync(clip),
            videos.Coordinator,
            videos.PlayerFactory);
        var domain = DomainFromSnapshot(snapshot) with
        {
            Source = new RecordedSessionSourceSnapshot(snapshot.Id, RecordedSessionSourceKind.ImportedSst, "ride.SST", 1, "source-hash")
        };
        var editor = harness.CreateRecordedEditor(snapshot, projectionChanges: Observable.Return(domain));
        await harness.LoadAsync(editor);
        return (harness, editor, clip);
    }

    private static TelemetryPlotContextMenuAction FindSignalAction(SessionDetailViewModel editor, string actionId) =>
        editor.SignalsWorkspace.SignalPlotContextMenuActionsBySignalRowId[SignalRowIds.Travel]
            .Single(action => action.Id == actionId);

    private static void ExecuteSignalAction(SessionDetailViewModel editor, string actionId, double clickSeconds) =>
        FindSignalAction(editor, actionId).Command.Execute(CreatePlotContext(clickSeconds));

    private static TelemetryPlotContextMenuContext CreatePlotContext(double clickSeconds) =>
        new(SignalRowIds.Travel, clickSeconds, DurationSeconds: 10, AnalysisRange: null);

    private static RecordedSessionDomainSnapshot DomainFromSnapshot(
        SessionSnapshot snapshot,
        SessionStaleness? staleness = null) => new(
        snapshot,
        null,
        null,
        null,
        null,
        null,
        staleness ?? new SessionStaleness.Current());

    private static SessionDetailLoadResult IncompleteResult(Guid sessionId) =>
        new SessionDetailLoadResult.IncompleteLocalData(
            sessionId,
            new MissingSessionData(
                ProcessedTelemetryBlob: true,
                RecordedSourceMissing: false));

    private static SessionInsightsResult CreateAnalysisResult() =>
        new(
            SurfacePresentationState.Ready,
            [new SessionInsightsFinding(
                SessionInsightsCategory.DataQuality,
                SessionInsightsSeverity.Info,
                SessionInsightsConfidence.Low,
                "Analysis ready",
                "Telemetry was analyzed.",
                "Compare against the next run.",
                [])]);

    private static TelemetryData CreateVibrationTelemetry() =>
        TestTelemetryData.CreateWithImu();

    private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken) =>
        await task.WaitAsync(cancellationToken);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
