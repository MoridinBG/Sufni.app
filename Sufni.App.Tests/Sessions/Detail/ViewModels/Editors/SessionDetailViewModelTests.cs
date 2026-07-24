using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Infrastructure;
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
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

[Collection("Ui")]
public class SessionDetailViewModelTests
{
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
    public async Task Unloaded_DropsTrackCompletion_FromCanceledLoad()
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

        await harness.UnloadAsync(editor);
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
    }

    [AvaloniaFact]
    public async Task Save_OnFailed_AppendsErrorMessage()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(updated: 5);
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        editor.Name = "renamed";
        harness.SessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Failed("disk full"));

        await harness.SaveAsync(editor);

        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Delete_HappyPath_NavigatesBack()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession();
        var editor = harness.CreateLoadedRecordedSession(snapshot);
        harness.SessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Deleted));

        await harness.DeleteAsync(editor);

        harness.Shell.Received(1).GoBack();
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Unloaded_CancelsInFlightLoad_AndDropsResult()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var pending = new TaskCompletionSource<SessionDetailLoadResult>();
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            loadResult: IncompleteResult(snapshot.Id));
        harness.SessionCoordinator.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<SessionPresentationDimensions>(),
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(3)));

        var loadTask = harness.LoadAsync(editor);
        await Task.Yield();

        await harness.UnloadAsync(editor);
        pending.SetResult(harness.CreateLoadedResult(TestTelemetryData.CreateProcessed()));
        await loadTask;

        Assert.Null(editor.CurrentTelemetryData);
        Assert.False(editor.IsComplete);
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
                Arg.Any<SessionPresentationDimensions>(),
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
                Arg.Any<SessionPresentationDimensions>(),
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

        Assert.False(editor.IsComplete);
        Assert.Equal(SessionScreenStateKind.IncompleteLocalData, editor.ScreenState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.MediaWorkspace.MapState.Kind);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_InitializesRecordedSessionExtensionScope_AndUnloadedDisposesIt()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var factory = new TestRecordedSessionExtensionFactory("test");
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            IncompleteResult(snapshot.Id),
            recordedSessionExtensionFactories: [factory]);

        await harness.LoadAsync(editor);

        Assert.NotNull(factory.Scope);
        Assert.True(factory.Scope.Initialized);
        Assert.Contains(factory.Scope.UpdatedStates, state => state.Identity.IsLoaded);

        await harness.UnloadAsync(editor);

        Assert.True(factory.Scope.Disposed);
        Assert.False(factory.Scope.UpdatedStates.Last().Identity.IsLoaded);
    }

    [AvaloniaFact]
    public async Task Loaded_RetainsProcessedTelemetry_AndUnloadedReleasesIt()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: false);
        var editor = harness.CreateLoadedRecordedSession(snapshot, IncompleteResult(snapshot.Id));

        await harness.LoadAsync(editor);

        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.RetainedSessionIds);
        Assert.Empty(harness.ProcessedTelemetryReader.ReleasedSessionIds);

        await harness.UnloadAsync(editor);

        Assert.Equal([snapshot.Id], harness.ProcessedTelemetryReader.ReleasedSessionIds);
    }

    [AvaloniaFact]
    public async Task RecordedSessionHostContext_RoutesHostCommands()
    {
        var harness = new SessionDetailHarness();
        var snapshot = harness.CreateRecordedSession(hasProcessedData: true);
        var factory = new TestRecordedSessionExtensionFactory("test");
        var telemetry = TestTelemetryData.CreateProcessed();
        var selectedRange = new TelemetryTimeRange(0.05, 0.2);
        var editor = harness.CreateLoadedRecordedSession(
            snapshot,
            harness.CreateLoadedResult(telemetry),
            recordedSessionExtensionFactories: [factory]);

        await harness.LoadAsync(editor);
        var context = factory.Context!;

        context.SetAnalysisRange(selectedRange.StartSeconds, selectedRange.EndSeconds);
        context.SetTimelineVisibleRange(0.2, 0.8, context);
        context.AddError("extension error");
        context.AddNotification("extension notification");
        var lease = context.StartOperation("Extension work");
        lease.Report("Extension still working", 50);
        var beganExternalAlignment = context.TryBeginTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.ExternalMedia,
            12.0,
            subjectId: "media-a");
        var beganGpsAlignment = context.TryBeginTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.GpsTrack,
            4.0);
        var pendingAlignment = factory.Scope!.UpdatedStates.Last().Timeline.Alignment.PendingMark;
        var resolvedExternalAlignment = context.TryResolveTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.ExternalMedia,
            7.0,
            out var alignmentResolution,
            subjectId: "media-a");

        Assert.Equal(selectedRange, editor.CurrentAnalysisRange);
        Assert.Equal(0.2, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.8, editor.Timeline.VisibleRangeEnd, 6);
        Assert.Contains("extension error", editor.ErrorMessages);
        Assert.Contains("extension notification", editor.Notifications);
        Assert.True(editor.SessionOperationState.IsVisible);
        Assert.Equal("Extension still working", editor.SessionOperationState.Message);
        Assert.Equal(50, editor.SessionOperationState.Percent);
        Assert.True(beganExternalAlignment);
        Assert.False(beganGpsAlignment);
        Assert.NotNull(pendingAlignment);
        Assert.Equal(RecordedSessionTimelineAlignmentTarget.ExternalMedia, pendingAlignment.Target);
        Assert.Equal("media-a", pendingAlignment.SubjectId);
        Assert.True(resolvedExternalAlignment);
        Assert.NotNull(alignmentResolution);
        Assert.Equal(5.0, alignmentResolution.OffsetDeltaSeconds);
        Assert.Null(factory.Scope.UpdatedStates.Last().Timeline.Alignment.PendingMark);

        lease.Complete();

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
            Arg.Any<SessionPresentationDimensions>(),
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
                Arg.Any<SessionPresentationDimensions>(),
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
            Arg.Any<SessionPresentationDimensions>(),
            Arg.Any<IProgress<SessionDetailLoadProgress>>(),
            Arg.Any<CancellationToken>());
        await harness.DialogService.Received(1).ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);
    }

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
