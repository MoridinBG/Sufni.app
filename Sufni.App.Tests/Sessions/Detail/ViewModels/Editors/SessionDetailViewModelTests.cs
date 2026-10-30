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
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Models;
using Sufni.App.Video.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

// Includes Avalonia video doubles and dispatcher-driven editor updates.
[Collection("Ui")]
public class SessionDetailViewModelTests
{
    [AvaloniaFact]
    public async Task LoadedSession_ProjectsTheFilterOptionFromTheSessionRow()
    {
        var harness = new SessionDetailHarness();
        var snapshot = TestSnapshots.Session(hasProcessedData: true) with { VelocityFilterWindowMilliseconds = 100 };
        var editor = harness.CreateLoadedRecordedSession(snapshot);

        await harness.LoadAsync(editor);

        Assert.Equal(100, editor.PreferencesPage.VelocityFilterWindowMilliseconds);
    }

    [AvaloniaFact]
    public async Task LoadedSession_ExposesWorkspaceSurfaces()
    {
        var harness = new SessionDetailHarness();
        var editor = harness.CreateLoadedRecordedSession();

        await harness.LoadAsync(editor);

        Assert.NotNull(editor.SignalsWorkspace);
        Assert.NotNull(editor.MediaWorkspace);
        Assert.NotNull(editor.AnalysisWorkspace);
        Assert.True(editor.ScreenState.IsReady);
    }

    [AvaloniaFact]
    public async Task InactiveTab_RetainsLoadAcrossViewReattach_AndDefersStalenessPrompt()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var editor = harness.CreateLoadedRecordedSession(snapshot, projectionChanges: changes);
        var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.DialogService.ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(_ =>
            {
                prompted.TrySetResult();
                return Task.FromResult<string?>(null);
            });
        editor.SetTabActive(false);
        await harness.LoadAsync(editor);
        changes.OnNext(DomainFromSnapshot(snapshot, staleness: new SessionStaleness.DependencyHashChanged()));

        await harness.LoadAsync(editor);

        Assert.False(prompted.Task.IsCompleted);
        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.RetainedSessionIds);
        Assert.Empty(harness.ProcessedTelemetryReader.ReleasedSessionIds);
        await harness.SessionCoordinator.Received(1).LoadDetailAsync(snapshot.Id,
            Arg.Any<IProgress<SessionDetailLoadProgress>>(), Arg.Any<CancellationToken>());

        editor.SetTabActive(true);
        await prompted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await harness.CloseAsync(editor);
        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.ReleasedSessionIds);
    }

    [AvaloniaFact]
    public async Task Loaded_PublishesTelemetryBeforeTrackCompletes()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackPoints = new List<TrackPoint> { new(2, 2, 2, 0) };
        var pendingTrack = new TaskCompletionSource<SessionDetailTrackLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(telemetry));
        harness.SessionCoordinator.LoadTrackAsync(
                snapshot.Id,
                telemetry,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(pendingTrack.Task);

        var loadTask = harness.LoadAsync(editor);
        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, telemetry));

        Assert.False(loadTask.IsCompleted);
        Assert.Same(telemetry, editor.SignalsWorkspace.TelemetryData);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Null(editor.CurrentTrackPoints);

        pendingTrack.SetResult(new SessionDetailTrackLoadResult.Loaded(
            new SessionTrackPresentationData(
                Guid.NewGuid(),
                [new TrackPoint(1, 1, 1, 0)],
                trackPoints,
                400.0)));
        await loadTask;

        Assert.Equal(trackPoints, editor.CurrentTrackPoints);
        Assert.Same(telemetry, editor.CurrentTelemetryData);
    }

    [AvaloniaFact]
    public async Task Closed_DropsTrackCompletion_FromCanceledLoad()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var telemetry = TestTelemetryData.CreateProcessed();
        var pendingTrack = new TaskCompletionSource<SessionDetailTrackLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(telemetry));
        harness.SessionCoordinator.LoadTrackAsync(
                snapshot.Id,
                telemetry,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(pendingTrack.Task);

        var loadTask = harness.LoadAsync(editor);
        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, telemetry));

        await harness.CloseAsync(editor);
        pendingTrack.SetResult(new SessionDetailTrackLoadResult.Loaded(
            new SessionTrackPresentationData(
                Guid.NewGuid(),
                [new TrackPoint(1, 1, 1, 0)],
                [new TrackPoint(2, 2, 2, 0)],
                400.0)));
        await loadTask;

        Assert.Same(telemetry, editor.CurrentTelemetryData);
        Assert.Null(editor.CurrentTrackPoints);
    }

    [AvaloniaFact]
    public async Task TrackFailure_ReplacesFirstContentWithFailedState()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var telemetry = TestTelemetryData.CreateProcessed();
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(telemetry));
        harness.SessionCoordinator.LoadTrackAsync(
                snapshot.Id,
                telemetry,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SessionDetailTrackLoadResult.Failed("track failed"));

        await harness.LoadAsync(editor);

        Assert.Null(editor.CurrentTelemetryData);
        Assert.Null(editor.CurrentTrackPoints);
        Assert.Equal(SessionScreenStateKind.Error, editor.ScreenState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.TravelSignalState.Kind);
    }

    [AvaloniaFact]
    public async Task Save_CompletesEdit_AfterAcceptingSavedBaseline()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(updated: 5);
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        editor.Name = "renamed";
        harness.SessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(snapshot.Id, 11));

        await harness.SaveAsync(editor);

        Assert.Equal(11, editor.BaselineUpdated);
        Assert.False(editor.IsDirty);
        harness.Shell.Received(1).CompleteEdit(new EditorKey.Session(snapshot.Id));
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(name: "old", updated: 5);
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Session(id: snapshot.Id, name: "remote-updated", updated: 12);
        harness.SessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Conflict(fresh));
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await harness.SaveAsync(editor);

        Assert.Equal("remote-updated", editor.Name);
        Assert.Equal(12, editor.BaselineUpdated);
        harness.Shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_Unsuccessful_KeepsDraftAndDistinguishesMissingSession(bool missing)
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(updated: 5);
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        editor.Name = "renamed";
        harness.SessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(missing ? new SessionSaveResult.Missing() : new SessionSaveResult.Failed(SessionSaveFailureReason.Save, "disk full"));

        await harness.SaveAsync(editor);

        Assert.True(editor.IsDirty);
        Assert.Equal(5, editor.BaselineUpdated);
        Assert.Equal(missing ? 0 : 1, editor.ErrorMessages.Count);
        Assert.Equal(missing ? 1 : 0, editor.Notifications.Count);
        harness.Shell.DidNotReceive().CompleteEdit(Arg.Any<EditorKey>());
    }

    [AvaloniaFact]
    public async Task Delete_HappyPath_NavigatesBack()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        harness.SessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult.Deleted());

        await harness.DeleteAsync(editor);

        await harness.Shell.Received(1).CloseEditorAsync(new EditorKey.Session(editor.Id));
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Closed_CancelsInFlightLoad_AndDropsResult()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var pending = new TaskCompletionSource<SessionDetailLoadResult>();
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            loadResult: IncompleteResult(snapshot.Id));
        harness.SessionCoordinator.LoadDetailAsync(
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

        Assert.Null(editor.CurrentTelemetryData);
    }

    [AvaloniaFact]
    public async Task SupersededTrackCompletion_DoesNotReplaceLatestGeneration()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var firstTelemetry = TestTelemetryData.CreateProcessed();
        var latestTelemetry = TestTelemetryData.CreateProcessed();
        var staleTrack = new List<TrackPoint> { new(1, 1, 1, 0) };
        var latestTrack = new List<TrackPoint> { new(2, 2, 2, 0) };
        var pendingTrack = new TaskCompletionSource<SessionDetailTrackLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var loadCount = 0;
        var trackLoadCount = 0;
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(firstTelemetry),
            watch.AsObservable());
        harness.SessionCoordinator.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++loadCount switch
            {
                1 => Task.FromResult<SessionDetailLoadResult>(harness.CreateLoadedResult(firstTelemetry)),
                2 => Task.FromResult<SessionDetailLoadResult>(harness.CreateLoadedResult(latestTelemetry)),
                _ => throw new InvalidOperationException("Unexpected load request."),
            });
        harness.SessionCoordinator.LoadTrackAsync(
                snapshot.Id,
                Arg.Any<TelemetryData>(),
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++trackLoadCount switch
            {
                1 => pendingTrack.Task,
                2 => Task.FromResult<SessionDetailTrackLoadResult>(
                    new SessionDetailTrackLoadResult.Loaded(
                        new SessionTrackPresentationData(Guid.NewGuid(), null, latestTrack, null))),
                _ => throw new InvalidOperationException("Unexpected track load request."),
            });

        var firstLoadTask = harness.LoadAsync(editor);
        await WaitForAsync(() => trackLoadCount == 1);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        watch.OnNext(DomainFromSnapshot(
            snapshot with { Updated = 2 },
            DerivedChangeKind.FingerprintChanged));
        await WaitForAsync(() => trackLoadCount == 2);
        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, latestTelemetry));
        await WaitForAsync(() => editor.CurrentTrackPoints?.SequenceEqual(latestTrack) == true);

        pendingTrack.SetResult(new SessionDetailTrackLoadResult.Loaded(
            new SessionTrackPresentationData(Guid.NewGuid(), null, staleTrack, null)));
        await firstLoadTask;

        Assert.Same(latestTelemetry, editor.CurrentTelemetryData);
        Assert.Equal(latestTrack, editor.CurrentTrackPoints);
    }

    [AvaloniaFact]
    public async Task SupersededLoadCompletion_DoesNotReplaceLatestTelemetry()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var firstRefresh = new TaskCompletionSource<SessionDetailLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestRefresh = new TaskCompletionSource<SessionDetailLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var initialTelemetry = TestTelemetryData.CreateProcessed();
        var staleTelemetry = TestTelemetryData.CreateProcessed();
        var latestTelemetry = TestTelemetryData.CreateProcessed();
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var loadCount = 0;
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(initialTelemetry),
            watch.AsObservable());
        harness.SessionCoordinator.LoadDetailAsync(
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
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        watch.OnNext(DomainFromSnapshot(
            snapshot with { Updated = 2 },
            DerivedChangeKind.FingerprintChanged));
        await WaitForAsync(() => loadCount == 2);
        watch.OnNext(DomainFromSnapshot(
            snapshot with { Updated = 3 },
            DerivedChangeKind.FingerprintChanged));
        await WaitForAsync(() => loadCount == 3);

        latestRefresh.SetResult(harness.CreateLoadedResult(latestTelemetry));
        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, latestTelemetry));
        firstRefresh.SetResult(harness.CreateLoadedResult(staleTelemetry));
        await Task.Yield();

        Assert.Same(latestTelemetry, editor.CurrentTelemetryData);
        Assert.Equal(3, loadCount);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenLocalDataIncomplete_EntersIncompleteScreenState()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var editor = harness.CreateLoadedRecordedSession(snapshot, IncompleteResult(snapshot.Id));

        await harness.LoadAsync(editor);

        Assert.Equal(SessionScreenStateKind.IncompleteLocalData, editor.ScreenState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.MediaWorkspace.MapState.Kind);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_RetainsProcessedTelemetry_AndClosingReleasesIt()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var editor = harness.CreateLoadedRecordedSession(snapshot, IncompleteResult(snapshot.Id));

        await harness.LoadAsync(editor);

        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.RetainedSessionIds);
        Assert.Empty(harness.ProcessedTelemetryReader.ReleasedSessionIds);

        await harness.CloseAsync(editor);

        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.ReleasedSessionIds);
    }

    [AvaloniaFact]
    public async Task SessionOperationState_FollowsTheSessionOperation()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var editor = harness.CreateLoadedRecordedSession(
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
    }

    [AvaloniaFact]
    public async Task SignalPreferenceChange_PersistsWithoutDirtyingSessionMetadata()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        Func<SessionPreferences, SessionPreferences>? update = null;
        harness.SessionPreferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(CreateVibrationTelemetry()));

        await harness.LoadAsync(editor);
        harness.SessionPreferences.ClearReceivedCalls();

        editor.PreferencesPage.VelocitySignal.SelectedSmoothing = PlotSmoothingLevel.Strong;

        Assert.False(editor.IsDirty);
        await harness.SessionPreferences.Received(1).UpdateRecordedAsync(
            snapshot.Id,
            Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        var updatedPreferences = update!(SessionPreferences.Default);
        Assert.Equal(PlotSmoothingLevel.Strong, updatedPreferences.SignalDisplay.VelocitySmoothing);
    }

    [AvaloniaFact]
    public async Task SelectingInsightsPage_RequestsInsightsAfterTelemetryIsAvailable()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var analysis = CreateAnalysisResult();
        harness.SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(analysis);
        var editor = harness.CreateLoadedRecordedSession(snapshot, harness.CreateLoadedResult(telemetry));

        await harness.LoadAsync(editor);
        harness.SessionInsightsService.ClearReceivedCalls();

        editor.MobileWorkspace.SelectedPageIndex = editor.Pages
            .Select((page, index) => (page, index))
            .Single(entry => entry.page is SessionInsightsPageViewModel)
            .index;

        Assert.Same(analysis, editor.AnalysisWorkspace.SessionInsights);
        harness.SessionInsightsService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry)));
    }

    [AvaloniaFact]
    public async Task RuntimeFreshUpdate_DeclinedDirtyReload_KeepsDraftAndBaseline()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(
            name: "trail run",
            description: "persisted",
            hasProcessedData: true,
            updated: 5);
        var updatedSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(oldTelemetry),
            watch.AsObservable());

        await harness.LoadAsync(editor);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";

        watch.OnNext(DomainFromSnapshot(updatedSnapshot, DerivedChangeKind.SessionMetadataChanged));
        await Task.Yield();

        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);
        Assert.Same(oldTelemetry, editor.CurrentTelemetryData);
        await harness.SessionCoordinator.Received(1).LoadDetailAsync(
            snapshot.Id,
            Arg.Any<IProgress<SessionDetailLoadProgress>>(),
            Arg.Any<CancellationToken>());
        await harness.DialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task CombinedMetadataAndDerivedChange_RefreshesTelemetry_AndStillPromptsForMetadata()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(
            name: "trail run",
            description: "persisted",
            hasProcessedData: true,
            updated: 5);
        var combinedSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();
        var freshTelemetry = TestTelemetryData.CreateProcessed();
        var loadCount = 0;
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(oldTelemetry),
            watch.AsObservable());
        harness.SessionCoordinator.LoadDetailAsync(
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
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";

        watch.OnNext(DomainFromSnapshot(
            combinedSnapshot,
            DerivedChangeKind.FingerprintChanged | DerivedChangeKind.SessionMetadataChanged));

        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, freshTelemetry));

        await harness.SessionCoordinator.Received(2).LoadDetailAsync(
            snapshot.Id,
            Arg.Any<IProgress<SessionDetailLoadProgress>>(),
            Arg.Any<CancellationToken>());
        await harness.DialogService.Received(1).ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task VideoAlignment_ShiftsClipByDistanceBetweenMarks()
    {
        using var videos = new VideoFixture();
        var (editor, clip) = await CreateEditorWithVideoAsync(videos);
        videos.Coordinator.ShiftAlignmentAsync(clip.Id, Arg.Any<double>()).Returns(new VideoAlignmentResult.Updated());

        ExecuteSignalAction(editor, "video-mark-video-event", clickSeconds: 5);
        await ((IAsyncRelayCommand)FindSignalAction(editor, "video-mark-telemetry-event").Command)
            .ExecuteAsync(CreatePlotContext(clickSeconds: 3));

        await videos.Coordinator.Received(1).ShiftAlignmentAsync(clip.Id, 2);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task VideoAlignment_Cancel_DropsPendingVideoMark()
    {
        using var videos = new VideoFixture();
        var (editor, _) = await CreateEditorWithVideoAsync(videos);

        ExecuteSignalAction(editor, "video-mark-video-event", clickSeconds: 5);
        ExecuteSignalAction(editor, "video-cancel-alignment", clickSeconds: 5);

        var context = CreatePlotContext(clickSeconds: 3);
        Assert.False(FindSignalAction(editor, "video-mark-telemetry-event").Command.CanExecute(context));
        Assert.True(FindSignalAction(editor, "video-mark-video-event").Command.CanExecute(context));
    }

    [AvaloniaFact]
    public async Task Split_OpensBackgroundTabBeforeRecomputeCompletes()
    {
        var (harness, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
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
        Assert.Contains("Session split.", editor.Notifications);
    }

    [AvaloniaFact]
    public async Task TrimUntilEnd_WhenEditFails_ShowsError()
    {
        var (harness, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        harness.SessionEditingCoordinator.TrimUntilEndAsync(editor.Id, 4)
            .Returns(new SessionEditResult.Failed(SessionEditFailureReason.Rejected, "Session changed while editing."));

        await ((IAsyncRelayCommand)FindSignalAction(editor, "session-trim-until-end").Command)
            .ExecuteAsync(CreatePlotContext(clickSeconds: 4));

        Assert.NotEmpty(editor.ErrorMessages);
        Assert.Empty(editor.Notifications);
    }

    [AvaloniaFact]
    public async Task TrimUntilEnd_WhenAppliedButProcessingFails_PreservesDiagnosticDetails()
    {
        var (harness, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        const string details = "calibration diagnostic";
        harness.SessionEditingCoordinator.TrimUntilEndAsync(editor.Id, 4)
            .Returns(new SessionEditResult.Applied(new SessionRecomputeResult.ProcessingFailed(
                SessionProcessingFailureReason.InvalidCalibration, details)));

        await ((IAsyncRelayCommand)FindSignalAction(editor, "session-trim-until-end").Command)
            .ExecuteAsync(CreatePlotContext(clickSeconds: 4));

        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
    }

    [AvaloniaFact]
    public async Task WindowEdits_KeepFiveSamplesInEveryRemainingPart()
    {
        var (_, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        var nearEnd = CreatePlotContext(clickSeconds: 9.998);

        Assert.True(FindSignalAction(editor, "session-trim-until-end").Command.CanExecute(nearEnd));
        Assert.False(FindSignalAction(editor, "session-split-here").Command.CanExecute(nearEnd));
        Assert.False(FindSignalAction(editor, "session-trim-from-beginning").Command.CanExecute(nearEnd));
    }

    [AvaloniaFact]
    public async Task UndoTrim_IsAvailableOnlyAfterTrim()
    {
        var trimmedSnapshot = TestSnapshots.Session(hasProcessedData: true);
        trimmedSnapshot = trimmedSnapshot with
        {
            SourceSessionId = trimmedSnapshot.Id,
            WindowEndSeconds = 8,
            PreviousWindowStartSeconds = 0
        };
        var (_, untrimmed) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        var (_, trimmed) = await CreateLoadedEditorAsync(trimmedSnapshot);
        var context = CreatePlotContext(clickSeconds: 3);

        Assert.False(FindSignalAction(untrimmed, "session-undo-trim").Command.CanExecute(context));
        Assert.True(FindSignalAction(trimmed, "session-undo-trim").Command.CanExecute(context));
    }

    [AvaloniaFact]
    public async Task SaveCurrentSelection_SavesTheAnalysisRangeAsSegment()
    {
        var (harness, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        harness.SegmentCoordinator.NextName(editor.Id).Returns("Segment 1");
        harness.SegmentCoordinator.ValidateName(editor.Id, "Segment 1", null).Returns((string?)null);
        harness.SegmentCoordinator.CreateAsync(editor.Id, "Segment 1", 2, 4).Returns(new SegmentChangeResult.Applied());
        harness.DialogService.ShowContentDialogAsync(Arg.Any<SegmentNamePromptViewModel>(), Arg.Any<DialogOptions>())
            .Returns(call =>
            {
                call.ArgAt<SegmentNamePromptViewModel>(0).ConfirmCommand.Execute(null);
                return PromptResult.Ok;
            });

        await ((IAsyncRelayCommand)FindSignalAction(editor, "save-current-selection").Command)
            .ExecuteAsync(CreatePlotContext(clickSeconds: 3) with { AnalysisRange = new TelemetryTimeRange(2, 4) });

        await harness.SegmentCoordinator.Received(1).CreateAsync(editor.Id, "Segment 1", 2, 4);
    }

    [AvaloniaFact]
    public async Task SaveCurrentSelection_IsUnavailableOutsideTheAnalysisRange()
    {
        var (_, editor) = await CreateLoadedEditorAsync(TestSnapshots.Session(hasProcessedData: true));
        var outside = CreatePlotContext(clickSeconds: 6) with { AnalysisRange = new TelemetryTimeRange(2, 4) };

        Assert.False(FindSignalAction(editor, "save-current-selection").Command.CanExecute(outside));
    }

    [AvaloniaFact]
    public async Task SegmentPicker_CanOpen_OnceTheTelemetryIsLoaded()
    {
        var harness = new SessionDetailHarness();
        var editor = harness.CreateLoadedRecordedSession(TestSnapshots.Session(hasProcessedData: true));
        var canOpenBeforeLoad = editor.SignalsWorkspace.SegmentPicker.CanOpen;

        await harness.LoadAsync(editor);

        Assert.False(canOpenBeforeLoad);
        Assert.True(editor.SignalsWorkspace.SegmentPicker.CanOpen);
    }

    private static async Task<(SessionDetailHarness Harness, SessionDetailViewModel Editor)> CreateLoadedEditorAsync(
        SessionSnapshot snapshot)
    {
        var harness = new SessionDetailHarness();
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        await harness.LoadAsync(editor);
        return (harness, editor);
    }

    private static async Task<(SessionDetailViewModel Editor, SessionVideoClip Clip)> CreateEditorWithVideoAsync(
        VideoFixture videos)
    {
        var harness = new SessionDetailHarness();
        var snapshot = TestSnapshots.Session(timestamp: 1_000, hasProcessedData: true);
        var clip = videos.CreateClip(snapshot.Id, startTimestamp: 1_000, durationSeconds: 60);
        harness.VideoPaneFactory = new VideoPaneFactory(
            await videos.CreateStoreAsync(clip),
            videos.Coordinator,
            videos.PlayerFactory);
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        await harness.LoadAsync(editor);
        return (editor, clip);
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
        DerivedChangeKind changeKind = DerivedChangeKind.None,
        SessionStaleness? staleness = null) => new(
        snapshot,
        null,
        null,
        null,
        null,
        null,
        staleness ?? new SessionStaleness.Current(),
        changeKind);

    private static SessionDetailLoadResult IncompleteResult(Guid sessionId) =>
        new SessionDetailLoadResult.IncompleteLocalData(
            sessionId,
            new MissingSessionData(
                ProcessedTelemetryBlob: true,
                RecordedSourceMissingOrHashMismatch: false));

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
