using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LiveSessionDetailViewModelTests
{
    private readonly ILiveSessionService liveSessionService = Substitute.For<ILiveSessionService>();
    private readonly SessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly Subject<LiveGraphBatch> graphBatches = new();
    private readonly LiveSessionCapturePackage capturePackage;

    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshots;
    private LiveSessionPresentationSnapshot currentSnapshot;

    public LiveSessionDetailViewModelTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        capturePackage = CreateCapturePackage();

        currentSnapshot = LiveSessionPresentationSnapshot.Empty;
        snapshots = new BehaviorSubject<LiveSessionPresentationSnapshot>(currentSnapshot);

        liveSessionService.Snapshots.Returns(snapshots);
        liveSessionService.GraphBatches.Returns(graphBatches);
        liveSessionService.Current.Returns(_ => currentSnapshot);
        liveSessionService.EnsureAttachedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        liveSessionService.PrepareCaptureForSaveAsync(Arg.Any<CancellationToken>()).Returns(capturePackage);
        liveSessionService.ResetCaptureAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentSnapshot = LiveSessionPresentationSnapshot.Empty;
            snapshots.OnNext(currentSnapshot);
            return Task.CompletedTask;
        });
        liveSessionService.DisposeAsync().Returns(ValueTask.CompletedTask);
        sessionCoordinator.SaveLiveCaptureAsync(
            Arg.Any<Session>(),
            Arg.Any<LiveSessionCapturePackage>(),
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>())
            .Returns(new LiveSessionSaveResult.Saved(Guid.NewGuid(), 5));
    }

    [AvaloniaFact]
    public async Task Loaded_InitializesMap_AndAttachesService()
    {
        var editor = CreateEditor();

        await editor.LoadedCommand.ExecuteAsync(null);

        await tileLayerService.Received(1).InitializeAsync();
        await liveSessionService.Received(1).EnsureAttachedAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void Construction_ExposesHiddenSessionAnalysisForV1()
    {
        var editor = CreateEditor();

        Assert.Equal(SurfacePresentationState.Hidden, editor.SessionAnalysis.State);
        Assert.Empty(editor.SessionAnalysis.Findings);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, editor.SelectedSessionAnalysisTargetProfile);
        Assert.Equal([SessionAnalysisTargetProfile.Weekend, SessionAnalysisTargetProfile.Trail, SessionAnalysisTargetProfile.Enduro, SessionAnalysisTargetProfile.DH], editor.SessionAnalysisTargetProfileOptions.Select(option => option.Value));
        Assert.Equal("Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith", editor.SessionAnalysisModesText);

        editor.SelectedTravelHistogramMode = TravelHistogramMode.DynamicSag;
        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        editor.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Travel;

        Assert.Equal("Travel: Dynamic sag  Velocity: Stroke-peak average  Balance: Travel", editor.SessionAnalysisModesText);
    }

    [AvaloniaFact]
    public async Task Reload_AfterUnload_ResubscribesToSnapshots()
    {
        var editor = CreateEditor();

        await editor.LoadedCommand.ExecuteAsync(null);
        await editor.UnloadedCommand.ExecuteAsync(null);
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Same(currentSnapshot.StatisticsTelemetry, editor.TelemetryData);
    }

    [AvaloniaFact]
    public async Task SnapshotUpdate_ProjectsTelemetryTrack_AndEnablesSave()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        var telemetryData = TestTelemetryData.Create();
        var trackPoints = new List<TrackPoint>
        {
            new(1, 2, 3, 4),
            new(2, 3, 4, 5),
        };

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData, trackPoints);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Same(telemetryData, editor.TelemetryData);
        Assert.Equal(currentSnapshot.Controls.SessionHeader!.SessionStartUtc.LocalDateTime, editor.Timestamp);
        Assert.Equal(2, editor.MediaWorkspace.MapViewModel!.SessionTrackPoints!.Count);
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task SnapshotUpdate_UsesWaitingStates_ForExpectedLiveSurfacesBeforeDataArrives()
    {
        var editor = CreateEditor(CreateSessionContext(hasFrontTravelCalibration: true, hasRearTravelCalibration: true));
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: false);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.GraphWorkspace.TravelGraphState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.GraphWorkspace.VelocityGraphState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.GraphWorkspace.ImuGraphState.Kind);
        Assert.True(editor.PreferencesPage.TravelPlot.Available);
        Assert.True(editor.PreferencesPage.VelocityPlot.Available);
        Assert.True(editor.PreferencesPage.ImuPlot.Available);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.ReboundBalanceState.Kind);
    }

    [AvaloniaFact]
    public async Task GraphBatches_PromoteTravelAndImuStates_Independently()
    {
        var editor = CreateEditor(CreateSessionContext(hasFrontTravelCalibration: true, hasRearTravelCalibration: true));
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: false);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        graphBatches.OnNext(CreateTravelOnlyBatch(revision: 1));
        await WaitForUiRefreshAsync();

        Assert.Equal(SurfaceStateKind.Ready, editor.GraphWorkspace.TravelGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.GraphWorkspace.VelocityGraphState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.GraphWorkspace.ImuGraphState.Kind);

        graphBatches.OnNext(CreateImuOnlyBatch(revision: 2));
        await WaitForUiRefreshAsync();

        Assert.Equal(SurfaceStateKind.Ready, editor.GraphWorkspace.ImuGraphState.Kind);
    }

    [AvaloniaFact]
    public async Task PlotPreferenceChange_UpdatesLiveGraphStateWithoutChangingDirtyState()
    {
        var editor = CreateEditor(CreateSessionContext(hasFrontTravelCalibration: true, hasRearTravelCalibration: true));
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: false);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        graphBatches.OnNext(CreateTravelOnlyBatch(revision: 1));
        await WaitForUiRefreshAsync();

        var wasDirty = editor.IsDirty;

        editor.PreferencesPage.VelocityPlot.Selected = false;

        Assert.Equal(wasDirty, editor.IsDirty);
        Assert.True(editor.GraphWorkspace.TravelGraphState.IsReady);
        Assert.True(editor.GraphWorkspace.VelocityGraphState.IsHidden);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.GraphWorkspace.ImuGraphState.Kind);
    }

    [AvaloniaFact]
    public async Task SnapshotUpdate_TransitionsMapState_FromWaitingToReady_WhenTrackPointsArrive()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: false, trackPoints: []);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.MediaWorkspace.MapState.Kind);

        currentSnapshot = CreateSnapshot(
            canSave: false,
            trackPoints:
            [
                new TrackPoint(1, 2, 0, 1),
                new TrackPoint(2, 3, 1, 2),
            ]);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Equal(SurfaceStateKind.Ready, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
    }

    [AvaloniaFact]
    public async Task SnapshotUpdate_KeepsStatisticsHidden_ForUnconfiguredSide()
    {
        var editor = CreateEditor(CreateSessionContext(hasFrontTravelCalibration: true, hasRearTravelCalibration: false));
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: false);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.ReboundBalanceState.Kind);
    }

    [AvaloniaFact]
    public async Task SaveCommand_UsesCustomName_AndResetsLiveCapture()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        editor.Name = "Morning lap";
        editor.DescriptionText = "first lap";
        editor.ForkSettings.SpringRate = "550 lb/in";
        editor.PreferencesPage.VelocityPlot.Selected = false;
        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        await editor.SaveCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await liveSessionService.Received(1).PrepareCaptureForSaveAsync(Arg.Any<CancellationToken>());
        await liveSessionService.Received(1).ResetCaptureAsync(Arg.Any<CancellationToken>());
        await sessionCoordinator.Received(1).SaveLiveCaptureAsync(
            Arg.Is<Session>(session =>
                session.Name == "Morning lap"
                && session.Description == "first lap"
                && session.Setup == editor.SetupId
                && session.Timestamp == capturePackage.TelemetryCapture.Metadata.Timestamp
                && session.FrontSpringRate == "550 lb/in"),
            capturePackage,
            Arg.Is<SessionPreferences>(preferences =>
                preferences.Plots.Travel &&
                !preferences.Plots.Velocity &&
                preferences.Plots.Imu &&
                preferences.Statistics.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged),
            Arg.Any<CancellationToken>());
        Assert.Equal("Morning lap", editor.Name);
        Assert.Null(editor.TelemetryData);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ResetCommand.CanExecute(null));
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task SaveCommand_RefreshesAutoGeneratedNameAfterSave()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        editor.Name = "Live Session 01-01-2000 00:00:00";

        await editor.SaveCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await sessionCoordinator.Received(1).SaveLiveCaptureAsync(
            Arg.Is<Session>(session =>
                session.Name.StartsWith("Live Session ", StringComparison.Ordinal)
                && session.Name != "Live Session 01-01-2000 00:00:00"),
            capturePackage,
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>());
        Assert.StartsWith("Live Session ", editor.Name);
        Assert.NotEqual("Live Session 01-01-2000 00:00:00", editor.Name);
    }

    [AvaloniaFact]
    public async Task SaveCommand_WhenCanceled_DoesNotAppendErrorMessage()
    {
        liveSessionService
            .PrepareCaptureForSaveAsync(Arg.Any<CancellationToken>())
            .Returns<LiveSessionCapturePackage>(_ => throw new OperationCanceledException());

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        await editor.SaveCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Empty(editor.ErrorMessages);
        await sessionCoordinator.DidNotReceive().SaveLiveCaptureAsync(
            Arg.Any<Session>(),
            Arg.Any<LiveSessionCapturePackage>(),
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SaveCommand_WhenPostSaveCleanupFails_DisablesResavingPersistedCapture()
    {
        liveSessionService
            .ResetCaptureAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("reset failed"));

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create(), captureRevision: 7);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        await editor.SaveCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await sessionCoordinator.Received(1).SaveLiveCaptureAsync(
            Arg.Any<Session>(),
            capturePackage,
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>());
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));
        Assert.Contains(editor.ErrorMessages, message => message.Contains("reset failed", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task SnapshotUpdate_WithNewCaptureRevision_ReenablesSave_AfterCleanupFailure()
    {
        liveSessionService
            .ResetCaptureAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("reset failed"));

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create(), captureRevision: 7);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        await editor.SaveCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.False(editor.SaveCommand.CanExecute(null));

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create(), captureRevision: 8);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ResetCommand_ClearsCaptureButPreservesSidebarState()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(
            canSave: true,
            telemetryData: TestTelemetryData.Create(),
            trackPoints:
            [
                new TrackPoint(1, 2, 3, 4),
            ]);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

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

    [AvaloniaFact]
    public void Pages_Initial_IsLiveGraphSpringDamperNotesPreferences()
    {
        var editor = CreateEditor();

        Assert.Equal(
            ["Graph", "Spring", "Damper", "Notes", "Preferences"],
            editor.Pages.Select(page => page.DisplayName));
        Assert.IsType<LiveGraphPageViewModel>(editor.Pages[0]);
        Assert.IsType<SpringPageViewModel>(editor.Pages[1]);
        Assert.IsType<DamperPageViewModel>(editor.Pages[2]);
        Assert.IsType<NotesPageViewModel>(editor.Pages[3]);
        Assert.IsType<PreferencesPageViewModel>(editor.Pages[4]);
        Assert.DoesNotContain(editor.Pages, page => page is BalancePageViewModel);
    }

    [AvaloniaFact]
    public async Task Bake_WithNewStatisticsTelemetry_AndDimensions_PopulatesPageFields()
    {
        var bakeData = new SessionCachePresentationData(
            FrontTravelHistogram: "<svg id='front-travel' />",
            RearTravelHistogram: "<svg id='rear-travel' />",
            FrontVelocityHistogram: "<svg id='front-vel' />",
            RearVelocityHistogram: "<svg id='rear-vel' />",
            CompressionBalance: null,
            ReboundBalance: null,
            DamperPercentages: new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            BalanceAvailable: false);

        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(bakeData);
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        sessionPresentationService
            .Received(1)
            .BuildCachePresentation(
                Arg.Any<TelemetryData>(),
                Arg.Any<SessionPresentationDimensions>(),
                Arg.Any<CancellationToken>());
        Assert.Equal(bakeData.FrontTravelHistogram, editor.SpringPage.FrontTravelHistogram);
        Assert.Equal(bakeData.RearTravelHistogram, editor.SpringPage.RearTravelHistogram);
        Assert.Equal(bakeData.FrontVelocityHistogram, editor.DamperPage.FrontVelocityHistogram);
        Assert.Equal(bakeData.RearVelocityHistogram, editor.DamperPage.RearVelocityHistogram);
        Assert.Equal(1d, editor.DamperPage.FrontHscPercentage);
        Assert.Equal(8d, editor.DamperPage.RearHsrPercentage);
        Assert.Equal(1d, editor.DamperPercentages.FrontHscPercentage);
    }

    [AvaloniaFact]
    public async Task Bake_WithoutDimensions_DoesNotRun()
    {
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        sessionPresentationService
            .DidNotReceive()
            .BuildCachePresentation(
                Arg.Any<TelemetryData>(),
                Arg.Any<SessionPresentationDimensions>(),
                Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Bake_InsertsBalancePageBeforeNotes_WhenBothSidesConfigured_AndPagePersists()
    {
        var balanceData = new SessionCachePresentationData(
            FrontTravelHistogram: null,
            RearTravelHistogram: null,
            FrontVelocityHistogram: null,
            RearVelocityHistogram: null,
            CompressionBalance: "<svg id='compression' />",
            ReboundBalance: "<svg id='rebound' />",
            DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            BalanceAvailable: true);
        var noBalanceData = balanceData with
        {
            CompressionBalance = null,
            ReboundBalance = null,
            BalanceAvailable = false,
        };

        var returnValue = balanceData;
        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(_ => returnValue);
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Contains(editor.Pages, page => page is BalancePageViewModel);
        var balanceIndex = editor.Pages.IndexOf(editor.BalancePage);
        var notesIndex = editor.Pages.IndexOf(editor.NotesPage);
        Assert.True(balanceIndex < notesIndex);
        Assert.True(editor.BalancePage.CompressionBalanceState.IsReady);

        returnValue = noBalanceData;
        // Simulate a warm-up-style telemetry (strokes cleared) so the live
        // workspace's balance state becomes WaitingForData.
        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TelemetryWithoutStrokes(), captureRevision: 2);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        // Both sides are configured and the session header is accepted, so the
        // balance workspace stays in WaitingForData when SVGs aren't emitted.
        // The tab should remain visible during the warm-up and render the
        // waiting placeholder.
        Assert.Contains(editor.Pages, page => page is BalancePageViewModel);
        Assert.Equal(
            Sufni.App.Presentation.SurfaceStateKind.WaitingForData,
            editor.BalancePage.CompressionBalanceState.Kind);
    }

    [AvaloniaFact]
    public async Task Bake_OmitsBalancePage_WhenOneSideUnconfigured()
    {
        var bakeData = new SessionCachePresentationData(
            FrontTravelHistogram: null,
            RearTravelHistogram: null,
            FrontVelocityHistogram: null,
            RearVelocityHistogram: null,
            CompressionBalance: null,
            ReboundBalance: null,
            DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            BalanceAvailable: false);

        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(bakeData);
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor(CreateSessionContext(hasFrontTravelCalibration: true, hasRearTravelCalibration: false));
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.DoesNotContain(editor.Pages, page => page is BalancePageViewModel);
    }

    [AvaloniaFact]
    public async Task Bake_SecondSnapshot_CancelsInFlightToken_AndDoesNotApplyStaleResult()
    {
        var firstBakeGate = new TaskCompletionSource<SessionCachePresentationData>();
        var firstData = new SessionCachePresentationData(
            FrontTravelHistogram: "<svg id='first-front' />",
            RearTravelHistogram: null,
            FrontVelocityHistogram: null,
            RearVelocityHistogram: null,
            CompressionBalance: null,
            ReboundBalance: null,
            DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            BalanceAvailable: false);
        var secondData = firstData with
        {
            FrontTravelHistogram = "<svg id='second-front' />",
        };

        CancellationToken capturedFirstToken = default;
        var callCount = 0;
        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    capturedFirstToken = callInfo.Arg<CancellationToken>();
                    return firstBakeGate.Task.Result;
                }

                return secondData;
            });
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        var firstTelemetry = TestTelemetryData.Create();
        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: firstTelemetry);
        snapshots.OnNext(currentSnapshot);

        // Wait for the runner to actually pick up the first bake — poll until the
        // first call reached the service (synchronous runner blocks inside the task
        // pool worker on firstBakeGate.Task.Result after this point).
        var waited = 0;
        while (callCount == 0 && waited < 2000)
        {
            await Task.Delay(25);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            waited += 25;
        }

        Assert.Equal(1, callCount);

        var secondTelemetry = TestTelemetryData.Create();
        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: secondTelemetry, captureRevision: 2);
        snapshots.OnNext(currentSnapshot);

        // Release the first bake — but by now the CTS for the first bake should be cancelled.
        firstBakeGate.SetResult(firstData);

        await WaitForUiRefreshAsync();
        await WaitForUiRefreshAsync();

        Assert.True(capturedFirstToken.IsCancellationRequested);
        Assert.Equal(secondData.FrontTravelHistogram, editor.SpringPage.FrontTravelHistogram);
    }

    [AvaloniaFact]
    public async Task NotesPageDescription_Change_FiresDescriptionTextInpc()
    {
        var editor = CreateEditor();
        var raised = new List<string?>();
        editor.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        editor.NotesPage.Description = "new notes";

        Assert.Contains("DescriptionText", raised);
        Assert.Equal("new notes", editor.DescriptionText);
    }

    [AvaloniaFact]
    public async Task NotesPageDescription_Change_RefreshesCommandState()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.True(editor.SaveCommand.CanExecute(null));
        var canExecuteChangedCount = 0;
        editor.SaveCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        editor.NotesPage.Description = "edit";

        Assert.True(canExecuteChangedCount > 0);
    }

    [AvaloniaFact]
    public async Task SaveCommand_WritesNotesPageFields_IntoPersistedSession()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        editor.NotesPage.Description = "lap notes";
        editor.NotesPage.ForkSettings.SpringRate = "520";
        editor.NotesPage.ShockSettings.HighSpeedCompression = 4;

        await editor.SaveCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await sessionCoordinator.Received(1).SaveLiveCaptureAsync(
            Arg.Is<Session>(session =>
                session.Description == "lap notes" &&
                session.FrontSpringRate == "520" &&
                session.RearHighSpeedCompression == 4),
            Arg.Any<LiveSessionCapturePackage>(),
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Bake_WarmUp_WithoutStrokeData_PushesWaitingState_OntoPageVMs()
    {
        var warmUpData = new SessionCachePresentationData(
            FrontTravelHistogram: null,
            RearTravelHistogram: null,
            FrontVelocityHistogram: null,
            RearVelocityHistogram: null,
            CompressionBalance: null,
            ReboundBalance: null,
            DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            BalanceAvailable: false);

        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(warmUpData);
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        // Telemetry with no stroke data yet — the live-VM workspace state is
        // WaitingForData, and the bake returns null SVGs because
        // HasStrokeData / HasBalanceData are false during warm-up.
        var warmUpTelemetry = TelemetryWithoutStrokes();
        currentSnapshot = CreateSnapshot(canSave: false, telemetryData: warmUpTelemetry);
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Equal(
            Sufni.App.Presentation.SurfaceStateKind.WaitingForData,
            editor.SpringPage.FrontHistogramState.Kind);
        Assert.Equal(
            Sufni.App.Presentation.SurfaceStateKind.WaitingForData,
            editor.DamperPage.RearHistogramState.Kind);
        Assert.Equal(
            Sufni.App.Presentation.SurfaceStateKind.WaitingForData,
            editor.BalancePage.CompressionBalanceState.Kind);
    }

    [AvaloniaFact]
    public async Task Reset_ClearsStaleHistograms_RemovesBalancePage_AndCancelsInFlightBake()
    {
        var bakedData = new SessionCachePresentationData(
            FrontTravelHistogram: "<svg id='front' />",
            RearTravelHistogram: "<svg id='rear' />",
            FrontVelocityHistogram: "<svg id='front-vel' />",
            RearVelocityHistogram: "<svg id='rear-vel' />",
            CompressionBalance: "<svg id='comp' />",
            ReboundBalance: "<svg id='reb' />",
            DamperPercentages: new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            BalanceAvailable: true);

        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(bakedData);
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);
        await WaitForUiRefreshAsync();

        Assert.Contains(editor.Pages, page => page is BalancePageViewModel);
        Assert.True(editor.SpringPage.FrontHistogramState.IsReady);
        Assert.Equal(1d, editor.DamperPage.FrontHscPercentage);

        await editor.ResetCommand.ExecuteAsync(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(editor.SpringPage.FrontTravelHistogram);
        Assert.Null(editor.SpringPage.RearTravelHistogram);
        Assert.Null(editor.DamperPage.FrontVelocityHistogram);
        Assert.Null(editor.DamperPage.RearVelocityHistogram);
        Assert.Null(editor.BalancePage.CompressionBalance);
        Assert.Null(editor.BalancePage.ReboundBalance);
        Assert.True(editor.SpringPage.FrontHistogramState.IsHidden);
        Assert.True(editor.DamperPage.FrontHistogramState.IsHidden);
        Assert.True(editor.BalancePage.CompressionBalanceState.IsHidden);
        Assert.Null(editor.DamperPage.FrontHscPercentage);
        Assert.Null(editor.DamperPercentages.FrontHscPercentage);
        Assert.DoesNotContain(editor.Pages, page => page is BalancePageViewModel);
    }

    [AvaloniaFact]
    public async Task Reset_DuringInFlightBake_DoesNotRepaintWithStaleData()
    {
        var bakeGate = new TaskCompletionSource<SessionCachePresentationData>();
        var staleData = new SessionCachePresentationData(
            FrontTravelHistogram: "<svg id='stale' />",
            RearTravelHistogram: null,
            FrontVelocityHistogram: null,
            RearVelocityHistogram: null,
            CompressionBalance: null,
            ReboundBalance: null,
            DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            BalanceAvailable: false);
        CancellationToken capturedBakeToken = default;

        sessionPresentationService
            .BuildCachePresentation(Arg.Any<TelemetryData>(), Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedBakeToken = callInfo.Arg<CancellationToken>();
                return bakeGate.Task.Result;
            });
        ConfigureRunnerToRunSynchronously();

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 800, 600));

        currentSnapshot = CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.Create());
        snapshots.OnNext(currentSnapshot);

        var waited = 0;
        while (capturedBakeToken == default && waited < 2000)
        {
            await Task.Delay(25);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            waited += 25;
        }

        await editor.ResetCommand.ExecuteAsync(null);
        bakeGate.SetResult(staleData);
        await WaitForUiRefreshAsync();

        Assert.True(capturedBakeToken.IsCancellationRequested);
        Assert.Null(editor.SpringPage.FrontTravelHistogram);
        Assert.True(editor.SpringPage.FrontHistogramState.IsHidden);
    }

    private static TelemetryData TelemetryWithoutStrokes()
    {
        // TestTelemetryData.Create() happens to produce strokes; we want a
        // warm-up-style telemetry with present front/rear but no strokes.
        var telemetry = TestTelemetryData.Create();
        telemetry.Front.Strokes.Compressions = [];
        telemetry.Front.Strokes.Rebounds = [];
        telemetry.Rear.Strokes.Compressions = [];
        telemetry.Rear.Strokes.Rebounds = [];
        return telemetry;
    }

    private void ConfigureRunnerToRunSynchronously()
    {
        backgroundTaskRunner
            .RunAsync(Arg.Any<Func<SessionCachePresentationData>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var work = callInfo.Arg<Func<SessionCachePresentationData>>();
                return Task.FromResult(work());
            });
    }

    private LiveSessionDetailViewModel CreateEditor(LiveDaqSessionContext? context = null)
    {
        return new LiveSessionDetailViewModel(
            context ?? CreateSessionContext(),
            liveSessionService,
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            tileLayerService,
            shell,
            dialogService);
    }

    private static LiveSessionPresentationSnapshot CreateSnapshot(
        bool canSave,
        TelemetryData? telemetryData = null,
        IReadOnlyList<TrackPoint>? trackPoints = null,
        LiveSessionHeader? header = null,
        long captureRevision = 1)
    {
        header ??= LiveProtocolTestFrames.CreateSessionHeaderModel();
        return new LiveSessionPresentationSnapshot(
            Stream: new LiveSessionStreamPresentation.Streaming(header.SessionStartUtc.LocalDateTime, header),
            StatisticsTelemetry: telemetryData,
            DamperPercentages: new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            SessionTrackPoints: trackPoints ?? [],
            Controls: new LiveSessionControlState(
                ConnectionState: LiveConnectionState.Connected,
                LastError: null,
                SessionHeader: header,
                CaptureStartUtc: header.SessionStartUtc,
                CaptureDuration: TimeSpan.FromSeconds(3),
                TravelQueueDepth: 0,
                ImuQueueDepth: 0,
                GpsQueueDepth: 0,
                TravelDroppedBatches: 0,
                ImuDroppedBatches: 0,
                GpsDroppedBatches: 0,
                CanSave: canSave),
            CaptureRevision: canSave ? captureRevision : 0);
    }

    private static LiveDaqSessionContext CreateSessionContext(
        bool hasFrontTravelCalibration = true,
        bool hasRearTravelCalibration = true)
    {
        return new LiveDaqSessionContext(
            IdentityKey: "board-1",
            BoardId: Guid.NewGuid(),
            DisplayName: "Board 1",
            SetupId: Guid.NewGuid(),
            SetupName: "race",
            BikeId: Guid.NewGuid(),
            BikeName: "demo",
            BikeData: new BikeData(63, 180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(
                hasFrontTravelCalibration ? CreateTravelCalibration(180) : null,
                hasRearTravelCalibration ? CreateTravelCalibration(170) : null));
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
                FrontMeasurements: [1000, 1010, 1020, 1030, 1040, 1050],
                RearMeasurements: [1100, 1110, 1120, 1130, 1140, 1150],
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
                Markers: []));
    }

    private static async Task WaitForUiRefreshAsync()
    {
        await Task.Delay(150);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static LiveGraphBatch CreateTravelOnlyBatch(long revision)
    {
        return new LiveGraphBatch(
            Revision: revision,
            TravelTimes: [0.0, 0.01],
            FrontTravel: [10.0, 11.0],
            RearTravel: [9.0, 10.0],
            VelocityTimes: [0.0, 0.01],
            FrontVelocity: [100.0, 110.0],
            RearVelocity: [90.0, 100.0],
            ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>(),
            ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>());
    }

    private static LiveGraphBatch CreateImuOnlyBatch(long revision)
    {
        return new LiveGraphBatch(
            Revision: revision,
            TravelTimes: [],
            FrontTravel: [],
            RearTravel: [],
            VelocityTimes: [],
            FrontVelocity: [],
            RearVelocity: [],
            ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [0.0, 0.01],
            },
            ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [1.0, 1.5],
            });
    }
}