using Sufni.App.Tests.TestSupport.LiveDaq;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Avalonia;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

public class LiveSessionDetailViewModelTests : IDisposable
{
    private readonly ManualUiTimerScheduler uiTimers;
    private readonly BikeStoreStub bikes = new();
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new();
    private readonly ILiveCaptureCoordinator liveCaptureCoordinator = Substitute.For<ILiveCaptureCoordinator>();
    private readonly ILiveDaqCoordinator liveDaqCoordinator = TestCoordinatorSubstitutes.LiveDaq();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly Subject<LiveSignalBatch> signalBatches = new();
    private readonly LiveSessionCapturePackage capturePackage;
    private readonly BehaviorSubject<LiveCaptureState> snapshots;
    private LiveCaptureState currentSnapshot;

    public LiveSessionDetailViewModelTests()
    {
        uiTimers = new ManualUiTimerScheduler();
        capturePackage = CreateCapturePackage();

        currentSnapshot = LiveCaptureState.Empty;
        snapshots = new BehaviorSubject<LiveCaptureState>(currentSnapshot);

        liveCaptureCoordinator.States.Returns(snapshots);
        liveCaptureCoordinator.SignalBatches.Returns(signalBatches);
        liveCaptureCoordinator.Current.Returns(_ => currentSnapshot);
        liveCaptureCoordinator.AttachAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<LivePreviewStartResult?>(null));
        liveCaptureCoordinator.PrepareForSaveAsync(Arg.Any<CancellationToken>()).Returns(capturePackage);
        liveCaptureCoordinator.ResetAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentSnapshot = LiveCaptureState.Empty;
            snapshots.OnNext(currentSnapshot);
            return Task.CompletedTask;
        });
        liveCaptureCoordinator.DisposeAsync().Returns(ValueTask.CompletedTask);
        sessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        liveDaqCoordinator.SaveCaptureAsync(
            Arg.Any<SessionSnapshot>(),
            Arg.Any<ILiveCaptureCoordinator>(),
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var revision = currentSnapshot.CaptureRevision;
                PublishSnapshot(LiveCaptureState.Empty);
                return new LiveSessionSaveResult.Saved(Guid.NewGuid(), revision);
            });
    }

    public void Dispose()
    {
        uiTimers.Dispose();
        bikes.Dispose();
        snapshots.Dispose();
        signalBatches.Dispose();
    }

    [Fact]
    public async Task TabDeactivation_StopsForegroundProjection_AndReactivationAppliesCurrentSnapshot()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        editor.SetTabActive(false);


        var telemetryData = TestTelemetryData.CreateProcessed();
        currentSnapshot = LiveCaptureTestData.State(canSave: true, telemetryData: telemetryData);
        snapshots.OnNext(currentSnapshot);
        Assert.Null(editor.AnalysisWorkspace.TelemetryData);

        editor.SetTabActive(true);

        Assert.NotNull(editor.AnalysisWorkspace.TelemetryData);
        Assert.True(editor.SaveCommand.CanExecute(null));
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task SnapshotUpdate_UsesWaitingMapState_WhenExpectedTrackHasNoPoints()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        PublishSnapshot(LiveCaptureTestData.State(canSave: false, trackPoints: []));

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
    }

    [Fact]
    public async Task ConnectionFailure_RendersDetailsWhileKeepingCaptureSaveable()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var snapshot = LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed());
        var closed = new LiveConnectionState.Closed(LiveStreamClosedReason.TransportFailure, "private socket detail");
        PublishSnapshot(snapshot with { Controls = snapshot.Controls with { ConnectionState = closed, LastError = closed.Message } });

        var failure = Assert.Single(editor.ErrorMessages);
        Assert.DoesNotContain("private socket detail", failure.Summary);
        Assert.Equal("private socket detail", failure.Details);
        Assert.True(editor.SaveCommand.CanExecute(null));
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task AnalysisModeChange_ForwardsOptionsAndRendersOnlyMatchingBackgroundResults()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var telemetry = TestTelemetryData.CreateProcessed();
        PublishSnapshot(LiveCaptureTestData.State(true, telemetry));
        var originalKey = editor.AnalysisWorkspace.AnalysisResultState.CurrentInputs!.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        Assert.NotNull(editor.AnalysisWorkspace.AnalysisResultState.Get(originalKey));

        editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        Assert.Null(editor.AnalysisWorkspace.AnalysisResultState.Get(originalKey));
        var next = LiveCaptureTestData.State(true, telemetry, captureRevision: 2,
            options: LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default) with { VelocityMode = VelocityAverageMode.StrokePeakAveraged });
        PublishSnapshot(next);

        var key = editor.AnalysisWorkspace.AnalysisResultState.CurrentInputs!.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        Assert.NotNull(editor.AnalysisWorkspace.AnalysisResultState.Get(key));
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task CaptureDuration_RemainsThePublishedDeviceDurationWhenWallClockAdvances()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var snapshot = LiveCaptureTestData.State(true);
        PublishSnapshot(snapshot);

        time.Advance(TimeSpan.FromMinutes(5));
        RefreshUi();

        Assert.Equal(snapshot.Controls.CaptureDuration, editor.ControlState.CaptureDuration);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task SaveCommand_UsesCustomNamePreferencesAndResetsLiveCapture()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(LiveCaptureTestData.State(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));

        editor.Name = "Morning lap";
        editor.DescriptionText = "first lap";
        editor.ForkSettings.SpringRate = "550 lb/in";
        editor.PreferencesPage.VelocitySignal.SelectedSmoothing = PlotSmoothingLevel.Strong;
        editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        var signalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Imu, isExpanded: false),
            new SignalLayoutRowPreferences(SignalRowIds.Travel),
        ]);
        editor.SignalsWorkspace.SignalLayoutPreferences = signalLayout;

        await editor.SaveCommand.ExecuteAsync(null);

        await liveDaqCoordinator.Received(1).SaveCaptureAsync(
            Arg.Is<SessionSnapshot>(session =>
                session.Name == "Morning lap"
                && session.Description == "first lap"
                && session.SetupId == editor.SetupId
                && session.FrontSpringRate == "550 lb/in"),
            liveCaptureCoordinator,
            Arg.Is<SessionPreferences>(preferences =>
                preferences.SignalDisplay.VelocitySmoothing == PlotSmoothingLevel.Strong &&
                preferences.Analysis.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged &&
                preferences.SignalLayout == signalLayout),
            Arg.Any<CancellationToken>());
        Assert.Equal("Morning lap", editor.Name);
        Assert.Null(editor.AnalysisWorkspace.TelemetryData);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ResetCommand.CanExecute(null));
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task SaveCommand_ProcessingFailure_PreservesCaptureForRetryAndSeparatesDetails()
    {
        const string details = "calibration diagnostic";
        liveDaqCoordinator.SaveCaptureAsync(Arg.Any<SessionSnapshot>(), Arg.Any<ILiveCaptureCoordinator>(),
                Arg.Any<SessionPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSessionSaveResult.ProcessingFailed(SessionProcessingFailureReason.InvalidCalibration, details));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var telemetry = TestTelemetryData.CreateProcessed();
        PublishSnapshot(LiveCaptureTestData.State(canSave: true, telemetryData: telemetry));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Same(telemetry, editor.AnalysisWorkspace.TelemetryData);
        Assert.True(editor.SaveCommand.CanExecute(null));
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
        shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task SaveCommand_NavigatesAndReportsWarning_WhenCommittedPublicationFails()
    {
        var savedId = Guid.NewGuid();
        liveDaqCoordinator.SaveCaptureAsync(
                Arg.Any<SessionSnapshot>(),
                Arg.Any<ILiveCaptureCoordinator>(),
                Arg.Any<SessionPreferences>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var revision = currentSnapshot.CaptureRevision;
                return new LiveSessionSaveResult.Saved(savedId, revision, "publish diagnostic");
            });
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(LiveCaptureTestData.State(
            canSave: true,
            telemetryData: TestTelemetryData.CreateProcessed()));

        await editor.SaveCommand.ExecuteAsync(null);

        shell.Received(1).OpenEditor(new EditorKey.Session(savedId));
        Assert.NotEmpty(editor.ErrorMessages);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task SaveCommand_CleanupFailureKeepsCaptureAndReportsSavedOutcomeWithoutNavigating()
    {
        const string details = "reset diagnostic";
        liveDaqCoordinator.SaveCaptureAsync(Arg.Any<SessionSnapshot>(), Arg.Any<ILiveCaptureCoordinator>(),
                Arg.Any<SessionPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSessionSaveResult.Saved(Guid.NewGuid(), 5, CleanupFailure: details));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var telemetry = TestTelemetryData.CreateProcessed();
        PublishSnapshot(LiveCaptureTestData.State(canSave: true, telemetryData: telemetry, captureRevision: 5));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(editor.AnalysisWorkspace.TelemetryData);
        Assert.Equal(telemetry.Front!.Travel, editor.AnalysisWorkspace.TelemetryData.Front!.Travel);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
        shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>(), Arg.Any<bool>());
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task SaveCleanupFailure_AllowsSavingNewerCaptureAlreadyPublished()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(LiveCaptureTestData.State(true, captureRevision: 1));
        liveDaqCoordinator.SaveCaptureAsync(Arg.Any<SessionSnapshot>(), Arg.Any<ILiveCaptureCoordinator>(),
            Arg.Any<SessionPreferences>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            PublishSnapshot(LiveCaptureTestData.State(true, captureRevision: 2));
            return new LiveSessionSaveResult.Saved(Guid.NewGuid(), 1, CleanupFailure: "reset failed");
        });

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.IsDirty);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task Close_WaitsForCanceledBakeAndIgnoresItsLateImage()
    {
        var completion = new TaskCompletionSource<SessionCachePresentationData>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        backgroundTaskRunner.RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                token = call.Arg<CancellationToken>();
                return completion.Task;
            });
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));
        PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));

        var close = editor.PrepareCloseAsync();
        Assert.True(token.IsCancellationRequested);
        Assert.False(close.IsCompleted);
        completion.SetResult(new SessionCachePresentationData("late image", null, null, null, null, null,
            SessionDampingPercentages.Empty, DampingSpeedCutoffs.Default, false));
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Null(editor.SpringPage.FrontTravelDistribution);
    }

    [Fact]
    public async Task Bake_NoncooperativeOldResultCannotReplaceNewerPresentation()
    {
        var first = new TaskCompletionSource<SessionCachePresentationData>();
        var old = new SessionCachePresentationData("<svg id='old' />", null, null, null, null, null,
            SessionDampingPercentages.Empty, DampingSpeedCutoffs.Default, false);
        var latest = old with { FrontTravelDistribution = "<svg id='latest' />" };
        var calls = 0;
        CancellationToken oldToken = default;
        backgroundTaskRunner.RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (++calls != 1) return Task.FromResult(latest);
                oldToken = call.Arg<CancellationToken>();
                return first.Task;
            });
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));
        PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));
        PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed(), captureRevision: 2));
        Assert.Equal(latest.FrontTravelDistribution, editor.SpringPage.FrontTravelDistribution);
        Assert.True(oldToken.IsCancellationRequested);
        first.SetResult(old);
        Assert.Equal(latest.FrontTravelDistribution, editor.SpringPage.FrontTravelDistribution);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task ForegroundBake_ResumesFromTheLatestStateAfterUnloading()
    {
        var first = new SessionCachePresentationData("first", null, null, null, null, null,
            SessionDampingPercentages.Empty, DampingSpeedCutoffs.Default, false);
        var latest = first with { FrontTravelDistribution = "latest" };
        backgroundTaskRunner.RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(first), Task.FromResult(latest));
        var editor = CreateEditor(isTabActive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));
        PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));
        Assert.Null(editor.SpringPage.FrontTravelDistribution);
        editor.SetTabActive(true);
        Assert.Equal("first", editor.SpringPage.FrontTravelDistribution);
        await editor.UnloadedCommand.ExecuteAsync(null);
        PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed(), captureRevision: 2));
        Assert.Equal("first", editor.SpringPage.FrontTravelDistribution);

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal("latest", editor.SpringPage.FrontTravelDistribution);
        await editor.PrepareCloseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalysisOptionChange_RetainsRenderedPagesWhileResultsRefresh(bool cutoffPreview)
    {
        backgroundTaskRunner.RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SessionCachePresentationData("spring", null, "damping", null, "balance", null,
                SessionDampingPercentages.Empty, DampingSpeedCutoffs.Default, true)));
        var editor = CreateEditor();
        try
        {
            await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));
            PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));
            Assert.Equal("damping", editor.DampingPage.FrontVelocityDistribution);

            if (cutoffPreview)
                editor.AnalysisWorkspace.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 700);
            else
                editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

            Assert.Equal("damping", editor.DampingPage.FrontVelocityDistribution);
            Assert.Contains(editor.BalancePage, editor.Pages);
        }
        finally { await editor.PrepareCloseAsync(); }
    }

    [Fact]
    public async Task RenderingFailure_IsReportedOnceForTheSameTelemetry()
    {
        backgroundTaskRunner.RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SessionCachePresentationData>(new InvalidOperationException("render detail")));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));
        var state = LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed());
        PublishSnapshot(state);
        PublishSnapshot(state with { Revision = 2, CaptureRevision = 2 });

        Assert.Single(editor.ErrorMessages);
        Assert.Null(editor.SpringPage.FrontTravelDistribution);
        Assert.True(editor.SaveCommand.CanExecute(null));
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task Reset_RemovesTheBalancePageAndKeepsMobileSelectionInRange()
    {
        backgroundTaskRunner.RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SessionCachePresentationData(null, null, null, null, "balance", null,
                SessionDampingPercentages.Empty, DampingSpeedCutoffs.Default, true)));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));
        PublishSnapshot(LiveCaptureTestData.State(true, TestTelemetryData.CreateProcessed()));
        Assert.Contains(editor.BalancePage, editor.Pages);
        var mobile = editor.ShellMobileWorkspace;
        mobile.SelectedPageIndex = int.MaxValue;
        Assert.Same(editor.PreferencesPage, mobile.SelectedPage);

        await editor.ResetCommand.ExecuteAsync(null);

        Assert.DoesNotContain(editor.BalancePage, editor.Pages);
        Assert.Same(editor.PreferencesPage, mobile.SelectedPage);
        mobile.SelectedPageIndex = -1;
        Assert.Same(editor.LiveSignalsPage, mobile.SelectedPage);
        await editor.PrepareCloseAsync();
    }

    private LiveSessionDetailViewModel CreateEditor(
        LiveDaqSessionContext? context = null,
        IBikeCoordinator? bikeCoordinator = null,
        bool isTabActive = true)
    {
        context ??= CreateSessionContext();
        if (bikes.Get(context.BikeId) is null) bikes.Add(TestSnapshots.Bike(context.BikeId));
        var editor = new LiveSessionDetailViewModel(
            context,
            liveCaptureCoordinator,
            liveDaqCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            new InlineUiThreadDispatcher(),
            time,
            bikes,
            bikeCoordinator ?? TestCoordinatorSubstitutes.Bike());
        editor.SetTabActive(isTabActive);
        return editor;
    }

    private void PublishSnapshot(LiveCaptureState snapshot)
    {
        currentSnapshot = snapshot;
        snapshots.OnNext(snapshot);
        RefreshUi();
    }

    private LiveDaqSessionContext CreateSessionContext(
        bool hasFrontTravelCalibration = true,
        bool hasRearTravelCalibration = true,
        Guid? bikeId = null,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        var resolvedBikeId = bikeId ?? Guid.NewGuid();
        bikes.Add(TestSnapshots.Bike(resolvedBikeId) with { DampingSpeedCutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default });
        return LiveCaptureTestData.Context(hasFrontTravelCalibration, hasRearTravelCalibration, resolvedBikeId);
    }

    private LiveSessionCapturePackage CreateCapturePackage()
    {
        var context = CreateSessionContext();
        return new LiveSessionCapturePackage(
            context,
            new LiveTelemetryCapture(
                Metadata: new Metadata
                {
                    SourceName = "live",
                    Version = 4,
                    SampleRate = 200,
                    Timestamp = 1_704_164_646,
                    Duration = 0.03,
                },
                BikeData: context.BikeData,
                FrontSegments: [new RawCountSegment { Counts = [1000, 1010, 1020, 1030, 1040, 1050] }],
                RearSegments: [new RawCountSegment { Counts = [1100, 1110, 1120, 1130, 1140, 1150] }],
                ImuData: null,
                GpsData:
                [
                    new GpsRecord(
                        Timestamp: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                        Latitude: 42.6977,
                        Longitude: 23.3219,
                        Altitude: 600,
                        Speed: 10,
                        Heading: 90,
                        FixMode: 3,
                        Satellites: 12,
                        Epe2d: 0.5f,
                        Epe3d: 0.8f),
                ],
                Markers: [],
                StreamGaps: [],
                FinalStatus: null,
                MissingFinalStatus: false));
    }

    private void RefreshUi()
    {
        if (uiTimers.HasScheduledTimers)
        {
            uiTimers.FireAll();
        }

    }
}
