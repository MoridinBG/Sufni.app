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
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new();
    private readonly ILiveSessionService liveSessionService = Substitute.For<ILiveSessionService>();
    private readonly ILiveDaqCoordinator liveDaqCoordinator = TestCoordinatorSubstitutes.LiveDaq();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly Subject<LiveSignalBatch> signalBatches = new();
    private readonly LiveSessionCapturePackage capturePackage;
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshots;
    private LiveSessionPresentationSnapshot currentSnapshot;

    public LiveSessionDetailViewModelTests()
    {
        uiTimers = new ManualUiTimerScheduler();
        capturePackage = CreateCapturePackage();

        currentSnapshot = LiveSessionPresentationSnapshot.Empty;
        snapshots = new BehaviorSubject<LiveSessionPresentationSnapshot>(currentSnapshot);

        liveSessionService.Snapshots.Returns(snapshots);
        liveSessionService.SignalBatches.Returns(signalBatches);
        liveSessionService.Current.Returns(_ => currentSnapshot);
        liveSessionService.EnsureAttachedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<LivePreviewStartResult?>(null));
        liveSessionService.PrepareCaptureForSaveAsync(Arg.Any<CancellationToken>()).Returns(capturePackage);
        liveSessionService.ResetCaptureAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentSnapshot = LiveSessionPresentationSnapshot.Empty;
            snapshots.OnNext(currentSnapshot);
            return Task.CompletedTask;
        });
        liveSessionService.DisposeAsync().Returns(ValueTask.CompletedTask);
        sessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        liveDaqCoordinator.SaveCaptureAsync(
            Arg.Any<SessionSnapshot>(),
            Arg.Any<ILiveSessionService>(),
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var revision = currentSnapshot.CaptureRevision;
                PublishSnapshot(LiveSessionPresentationSnapshot.Empty);
                return new LiveSessionSaveResult.Saved(Guid.NewGuid(), revision);
            });
    }

    public void Dispose()
    {
        uiTimers.Dispose();
        snapshots.Dispose();
        signalBatches.Dispose();
    }

    [Fact]
    public async Task LoadedCommand_AttachesCaptureWithoutStartingForegroundProjection_WhenInactive()
    {
        var editor = CreateEditor(isTabActive: false);

        await editor.LoadedCommand.ExecuteAsync(null);

        await liveSessionService.Received(1).EnsureAttachedAsync(Arg.Any<CancellationToken>());
        Assert.False(uiTimers.HasScheduledTimers);
        Assert.False(snapshots.HasObservers);
        Assert.False(signalBatches.HasObservers);
        await liveSessionService.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task LoadedCommand_WhenDaqPortBusy_ShowsNotificationAndRetainsCapture()
    {
        liveSessionService.EnsureAttachedAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<LivePreviewStartResult?>(new LivePreviewStartResult.Busy(new DaqTransportHolder.Import(Guid.NewGuid()))));
        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));
        var editor = CreateEditor();

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Single(editor.Notifications);
        Assert.Same(currentSnapshot.AnalysisTelemetry, editor.TelemetryData);
        await liveSessionService.DidNotReceive().ResetCaptureAsync(Arg.Any<CancellationToken>());
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task TabDeactivation_StopsForegroundProjection_AndReactivationAppliesCurrentSnapshot()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.True(uiTimers.HasScheduledTimers);
        Assert.True(snapshots.HasObservers);
        Assert.True(signalBatches.HasObservers);

        editor.SetTabActive(false);

        Assert.False(uiTimers.HasScheduledTimers);
        Assert.False(snapshots.HasObservers);
        Assert.False(signalBatches.HasObservers);
        await liveSessionService.DidNotReceive().DisposeAsync();
        await liveSessionService.DidNotReceive().ResetCaptureAsync(Arg.Any<CancellationToken>());

        var telemetryData = TestTelemetryData.CreateProcessed();
        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: telemetryData);
        snapshots.OnNext(currentSnapshot);
        Assert.Null(editor.TelemetryData);

        editor.SetTabActive(true);

        Assert.True(uiTimers.HasScheduledTimers);
        Assert.True(snapshots.HasObservers);
        Assert.True(signalBatches.HasObservers);
        Assert.Same(telemetryData, editor.TelemetryData);
    }

    [Fact]
    public async Task CloseAfterDeactivation_DisposesCaptureServiceExactlyOnce()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        editor.SetTabActive(false);

        await editor.PrepareCloseAsync();

        await liveSessionService.Received(1).DisposeAsync();
        await liveSessionService.DidNotReceive().ResetCaptureAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SnapshotUpdate_ProjectsTelemetryTrackAndSaveState()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var telemetryData = TestTelemetryData.CreateProcessed();
        var trackPoints = new List<TrackPoint>
        {
            new(1, 2, 3, 4),
            new(2, 3, 4, 5),
        };

        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData, trackPoints));

        Assert.Same(telemetryData, editor.TelemetryData);
        Assert.Equal(currentSnapshot.Controls.SessionHeader!.SessionStartUtc.LocalDateTime, editor.Timestamp);
        Assert.Equal(2, editor.MediaWorkspace.MapViewModel!.SessionTrackPoints!.Count);
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));
    }

    [Fact]
    public async Task SnapshotUpdate_UsesWaitingMapState_WhenExpectedTrackHasNoPoints()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        PublishSnapshot(CreateSnapshot(canSave: false, trackPoints: []));

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
    }

    [Fact]
    public async Task ConnectionFailure_RendersDetailsWhileKeepingCaptureSaveable()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var snapshot = CreateSnapshot(true, TestTelemetryData.CreateProcessed());
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
        PublishSnapshot(CreateSnapshot(true, telemetry));
        var originalKey = editor.AnalysisResultState.CurrentInputs!.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        Assert.NotNull(editor.AnalysisResultState.Get(originalKey));
        sessionPresentationService.ClearReceivedCalls();

        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        liveSessionService.Received().UpdateAnalysisOptions(Arg.Is<LiveAnalysisOptions>(options =>
            options.VelocityMode == VelocityAverageMode.StrokePeakAveraged));
        Assert.Null(editor.AnalysisResultState.Get(originalKey));
        var next = CreateSnapshot(true, telemetry, captureRevision: 2,
            options: LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default) with { VelocityMode = VelocityAverageMode.StrokePeakAveraged });
        PublishSnapshot(next);

        var key = editor.AnalysisResultState.CurrentInputs!.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        Assert.Same(next.Analysis!.Results[key], editor.AnalysisResultState.Get(key));
        Assert.Equal(next.DampingPercentages, editor.DampingPercentages);
        sessionPresentationService.DidNotReceive().CalculateDampingPercentages(
            Arg.Any<TelemetryData>(), Arg.Any<TelemetryTimeRange?>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs?>());
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task CaptureDuration_RemainsThePublishedDeviceDurationWhenWallClockAdvances()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var snapshot = CreateSnapshot(true);
        PublishSnapshot(snapshot);

        time.Advance(TimeSpan.FromMinutes(5));
        RefreshUi();

        Assert.Equal(snapshot.Controls.CaptureDuration, editor.ControlState.CaptureDuration);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task SaveCommand_RefreshesAutomaticNameUsingLocalClock()
    {
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        time.SetUtcNow(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));
        Assert.Equal("Live Session 02-01-2026 03:04:05", editor.Name);
        time.Advance(TimeSpan.FromMinutes(2));

        await editor.SaveCommand.ExecuteAsync(null);

        await liveDaqCoordinator.Received(1).SaveCaptureAsync(
            Arg.Is<SessionSnapshot>(session => session.Name == "Live Session 02-01-2026 03:06:05"),
            liveSessionService, Arg.Any<SessionPreferences>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_UsesCustomNamePreferencesAndResetsLiveCapture()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));

        editor.Name = "Morning lap";
        editor.DescriptionText = "first lap";
        editor.ForkSettings.SpringRate = "550 lb/in";
        editor.PreferencesPage.VelocitySignal.SelectedSmoothing = PlotSmoothingLevel.Strong;
        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
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
            liveSessionService,
            Arg.Is<SessionPreferences>(preferences =>
                preferences.SignalDisplay.Travel &&
                preferences.SignalDisplay.Velocity &&
                preferences.SignalDisplay.Imu &&
                preferences.SignalDisplay.VelocitySmoothing == PlotSmoothingLevel.Strong &&
                preferences.Analysis.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged &&
                preferences.SignalLayout == signalLayout),
            Arg.Any<CancellationToken>());
        Assert.Equal("Morning lap", editor.Name);
        Assert.Null(editor.TelemetryData);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ResetCommand.CanExecute(null));
        Assert.Empty(editor.ErrorMessages);
    }

    [Fact]
    public async Task SaveCommand_ProcessingFailure_PreservesCaptureForRetryAndSeparatesDetails()
    {
        const string details = "calibration diagnostic";
        liveDaqCoordinator.SaveCaptureAsync(Arg.Any<SessionSnapshot>(), Arg.Any<ILiveSessionService>(),
                Arg.Any<SessionPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSessionSaveResult.ProcessingFailed(SessionProcessingFailureReason.InvalidCalibration, details));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var telemetry = TestTelemetryData.CreateProcessed();
        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData: telemetry));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Same(telemetry, editor.TelemetryData);
        Assert.True(editor.SaveCommand.CanExecute(null));
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
        shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task SaveCommand_ResetsCaptureAndReportsWarning_WhenCommittedSavePublicationFails()
    {
        var savedId = Guid.NewGuid();
        liveDaqCoordinator.SaveCaptureAsync(
                Arg.Any<SessionSnapshot>(),
                Arg.Any<ILiveSessionService>(),
                Arg.Any<SessionPreferences>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var revision = currentSnapshot.CaptureRevision;
                PublishSnapshot(LiveSessionPresentationSnapshot.Empty);
                return new LiveSessionSaveResult.Saved(savedId, revision, "publish diagnostic");
            });
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(CreateSnapshot(
            canSave: true,
            telemetryData: TestTelemetryData.CreateProcessed()));

        await editor.SaveCommand.ExecuteAsync(null);

        shell.Received(1).OpenEditor(new EditorKey.Session(savedId));
        Assert.NotEmpty(editor.ErrorMessages);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveCommand_CleanupFailureKeepsCaptureAndReportsSavedOutcomeWithoutNavigating()
    {
        const string details = "reset diagnostic";
        liveDaqCoordinator.SaveCaptureAsync(Arg.Any<SessionSnapshot>(), Arg.Any<ILiveSessionService>(),
                Arg.Any<SessionPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSessionSaveResult.Saved(Guid.NewGuid(), 5, CleanupFailure: details));
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        var telemetry = TestTelemetryData.CreateProcessed();
        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData: telemetry, captureRevision: 5));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Same(telemetry, editor.TelemetryData);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
        shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ResetCommand_ClearsCaptureButPreservesSidebarState()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        PublishSnapshot(CreateSnapshot(
            canSave: true,
            telemetryData: TestTelemetryData.CreateProcessed(),
            trackPoints:
            [
                new TrackPoint(1, 2, 3, 4),
            ]));
        editor.Name = "Custom live session";
        editor.DescriptionText = "first lap";
        editor.ForkSettings.SpringRate = "550 lb/in";

        await editor.ResetCommand.ExecuteAsync(null);

        await liveSessionService.Received(1).ResetCaptureAsync(Arg.Any<CancellationToken>());
        Assert.Equal("Custom live session", editor.Name);
        Assert.Equal("first lap", editor.DescriptionText);
        Assert.Equal("550 lb/in", editor.ForkSettings.SpringRate);
        Assert.Null(editor.TelemetryData);
        Assert.Equal(TimeSpan.Zero, editor.ControlState.CaptureDuration);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ResetCommand.CanExecute(null));
    }

    [Fact]
    public async Task DampingSpeedCutoffCommit_PersistsThroughBikeCoordinator()
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        var savedCutoffs = initialCutoffs.With(SuspensionType.Front, DampingSpeedCircuit.Compression, 260);
        var savedBike = TestSnapshots.Bike(id: bikeId, updated: 8) with
        {
            FrontCompressionDampingCutoffMmPerSecond = savedCutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = savedCutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = savedCutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = savedCutoffs.Rear.ReboundMmPerSecond,
        };
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId,
                initialCutoffs.Front.CompressionMmPerSecond,
                SuspensionType.Front,
                DampingSpeedCircuit.Compression,
                260)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Saved(savedBike));
        var context = CreateSessionContext(
            bikeId: bikeId,
            dampingSpeedCutoffs: initialCutoffs);
        var editor = CreateEditor(context, bikeCoordinator);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        await bikeCoordinator.Received(1).UpdateDampingSpeedCutoffAsync(
            bikeId,
            initialCutoffs.Front.CompressionMmPerSecond,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            260);
        Assert.Equal(savedCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(savedCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.True(editor.CanEditDampingSpeedCutoffs);
        Assert.Empty(editor.ErrorMessages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DampingSpeedCutoffCommit_AdvancesExpectedValueFromSavedOrConflictResult(bool conflict)
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bike = TestSnapshots.Bike() with { FrontCompressionDampingCutoffMmPerSecond = 350 };
        var initial = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, 100, SuspensionType.Front, DampingSpeedCircuit.Compression, 350)
            .Returns(conflict ? new BikeDampingSpeedCutoffUpdateResult.Conflict(bike)
                : new BikeDampingSpeedCutoffUpdateResult.Saved(bike));
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, 350, SuspensionType.Front, DampingSpeedCircuit.Compression, 400)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Saved(bike with { FrontCompressionDampingCutoffMmPerSecond = 400 }));
        var editor = CreateEditor(CreateSessionContext(bikeId: bike.Id, dampingSpeedCutoffs: initial), bikeCoordinator);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 350);
        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 400);

        await bikeCoordinator.Received(1).UpdateDampingSpeedCutoffAsync(bike.Id, 350, SuspensionType.Front, DampingSpeedCircuit.Compression, 400);
        Assert.Equal(400, editor.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DampingSpeedCutoffCommitUnsuccessful_RestoresPersistedCutoffs(bool missingBike)
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId,
                initialCutoffs.Rear.ReboundMmPerSecond,
                SuspensionType.Rear,
                DampingSpeedCircuit.Rebound,
                600)
            .Returns(missingBike ? new BikeDampingSpeedCutoffUpdateResult.Missing()
                : new BikeDampingSpeedCutoffUpdateResult.Failed(BikeDampingCutoffFailureReason.Save, "disk full"));
        var context = CreateSessionContext(
            bikeId: bikeId,
            dampingSpeedCutoffs: initialCutoffs);
        var editor = CreateEditor(context, bikeCoordinator);
        editor.PreviewDampingSpeedCutoff(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 600);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 600);

        Assert.Equal(initialCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(initialCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.Equal(!missingBike, editor.CanEditDampingSpeedCutoffs);
        Assert.Equal(missingBike ? 0 : 1, editor.ErrorMessages.Count);
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
        PublishSnapshot(CreateSnapshot(true, TestTelemetryData.CreateProcessed()));

        var close = editor.PrepareCloseAsync();
        Assert.True(token.IsCancellationRequested);
        Assert.False(close.IsCompleted);
        completion.SetResult(new SessionCachePresentationData("late image", null, null, null, null, null,
            SessionDampingPercentages.Empty, DampingSpeedCutoffs.Default, false));
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Null(editor.SpringPage.FrontTravelDistribution);
    }

    [Fact]
    public async Task Bake_SecondSnapshot_CancelsInFlightTokenAndIgnoresStaleResult()
    {
        var firstBakeGate = new TaskCompletionSource<SessionCachePresentationData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstData = new SessionCachePresentationData(
            FrontTravelDistribution: "<svg id='first-front' />",
            RearTravelDistribution: null,
            FrontVelocityDistribution: null,
            RearVelocityDistribution: null,
            CompressionBalance: null,
            ReboundBalance: null,
            DampingPercentages: SessionDampingPercentages.Empty,
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            BalanceAvailable: false);
        var secondData = firstData with
        {
            FrontTravelDistribution = "<svg id='second-front' />",
        };
        var firstBakeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedFirstToken = default;
        var callCount = 0;
        sessionPresentationService
            .BuildCachePresentation(
                Arg.Any<TelemetryData>(),
                Arg.Any<SessionPresentationDimensions>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    capturedFirstToken = callInfo.Arg<CancellationToken>();
                    firstBakeStarted.TrySetResult();
                    return firstBakeGate.Task.WaitAsync(capturedFirstToken).GetAwaiter().GetResult();
                }

                return secondData;
            });
        ConfigureRunnerToRunOnBackgroundTask();
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        PublishSnapshot(CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));
        await firstBakeStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var secondBakeApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.SpringPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SpringPageViewModel.FrontTravelDistribution) &&
                editor.SpringPage.FrontTravelDistribution == secondData.FrontTravelDistribution)
            {
                secondBakeApplied.TrySetResult();
            }
        };

        PublishSnapshot(CreateSnapshot(
            canSave: true,
            telemetryData: TestTelemetryData.CreateProcessed(),
            captureRevision: 2));
        firstBakeGate.SetResult(firstData);
        RefreshUi();
        if (editor.SpringPage.FrontTravelDistribution != secondData.FrontTravelDistribution)
        {
            await secondBakeApplied.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        }

        Assert.True(capturedFirstToken.IsCancellationRequested);
        Assert.Equal(secondData.FrontTravelDistribution, editor.SpringPage.FrontTravelDistribution);
    }

    private void ConfigureRunnerToRunOnBackgroundTask()
    {
        backgroundTaskRunner
            .RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                Task.Run(
                    callInfo.Arg<Func<SessionCachePresentationData>>(),
                    callInfo.Arg<CancellationToken>()));
    }

    private LiveSessionDetailViewModel CreateEditor(
        LiveDaqSessionContext? context = null,
        IBikeCoordinator? bikeCoordinator = null,
        bool isTabActive = true)
    {
        var editor = new LiveSessionDetailViewModel(
            context ?? CreateSessionContext(),
            liveSessionService,
            liveDaqCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            new InlineUiThreadDispatcher(),
            time,
            uiTimers,
            bikeCoordinator);
        editor.SetTabActive(isTabActive);
        return editor;
    }

    private void PublishSnapshot(LiveSessionPresentationSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        snapshots.OnNext(snapshot);
        RefreshUi();
    }

    private static LiveSessionPresentationSnapshot CreateSnapshot(
        bool canSave,
        TelemetryData? telemetryData = null,
        IReadOnlyList<TrackPoint>? trackPoints = null,
        LiveSessionHeader? header = null,
        long captureRevision = 1,
        LiveAnalysisOptions? options = null)
    {
        header ??= LiveProtocolTestFrames.CreateSessionHeaderModel();
        return new LiveSessionPresentationSnapshot(
            AnalysisTelemetry: telemetryData,
            DampingPercentages: new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            SessionTrackPoints: trackPoints ?? [],
            Controls: new LiveSessionControlState(
                ConnectionState: new LiveConnectionState.Connected(),
                LastError: null,
                SessionHeader: header,
                CaptureStartUtc: header.SessionStartUtc,
                CaptureDuration: TimeSpan.FromSeconds(3),
                CanSave: canSave),
            CaptureRevision: canSave ? captureRevision : 0)
        {
            Analysis = telemetryData is null ? null : LiveSessionAnalysis.Compute(captureRevision,
                options ?? LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default), telemetryData,
                new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8), true, true)
        };
    }

    private static LiveDaqSessionContext CreateSessionContext(
        bool hasFrontTravelCalibration = true,
        bool hasRearTravelCalibration = true,
        Guid? bikeId = null,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        var resolvedBikeId = bikeId ?? Guid.NewGuid();
        return new LiveDaqSessionContext(
            IdentityKey: "board-1",
            BoardId: Guid.NewGuid(),
            DisplayName: "Board 1",
            SetupId: Guid.NewGuid(),
            SetupName: "race",
            BikeId: resolvedBikeId,
            BikeName: "demo",
            BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(
                hasFrontTravelCalibration ? CreateTravelCalibration(180) : null,
                hasRearTravelCalibration ? CreateTravelCalibration(170) : null),
            DampingSpeedCutoffs: dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default);
    }

    private static LiveDaqTravelChannelCalibration CreateTravelCalibration(double maxTravel)
    {
        return new LiveDaqTravelChannelCalibration(maxTravel, measurement => measurement);
    }

    private static LiveSessionCapturePackage CreateCapturePackage()
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
