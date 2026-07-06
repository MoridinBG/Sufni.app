using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

[Collection("Ui")]
public class LiveSessionDetailViewModelTests : IDisposable
{
    private readonly ManualPeriodicUiTimerScheduler uiTimers;
    private readonly ILiveSessionService liveSessionService = Substitute.For<ILiveSessionService>();
    private readonly ISessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly Subject<LiveSignalBatch> signalBatches = new();
    private readonly LiveSessionCapturePackage capturePackage;
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshots;
    private LiveSessionPresentationSnapshot currentSnapshot;

    public LiveSessionDetailViewModelTests()
    {
        uiTimers = ManualPeriodicUiTimerScheduler.Install();
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        capturePackage = CreateCapturePackage();

        currentSnapshot = LiveSessionPresentationSnapshot.Empty;
        snapshots = new BehaviorSubject<LiveSessionPresentationSnapshot>(currentSnapshot);

        liveSessionService.Snapshots.Returns(snapshots);
        liveSessionService.SignalBatches.Returns(signalBatches);
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
        sessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        sessionCoordinator.SaveLiveCaptureAsync(
            Arg.Any<Session>(),
            Arg.Any<LiveSessionCapturePackage>(),
            Arg.Any<SessionPreferences>(),
            Arg.Any<CancellationToken>())
            .Returns(new LiveSessionSaveResult.Saved(Guid.NewGuid(), 5));
    }

    public void Dispose()
    {
        uiTimers.Dispose();
        snapshots.Dispose();
        signalBatches.Dispose();
    }

    [AvaloniaFact]
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

        await PublishSnapshotAsync(CreateSnapshot(canSave: true, telemetryData, trackPoints));

        Assert.Same(telemetryData, editor.TelemetryData);
        Assert.Equal(currentSnapshot.Controls.SessionHeader!.SessionStartUtc.LocalDateTime, editor.Timestamp);
        Assert.Equal(2, editor.MediaWorkspace.MapViewModel!.SessionTrackPoints!.Count);
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task SnapshotUpdate_UsesWaitingMapState_WhenExpectedTrackHasNoPoints()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        await PublishSnapshotAsync(CreateSnapshot(canSave: false, trackPoints: []));

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
    }

    [AvaloniaFact]
    public async Task SaveCommand_UsesCustomNamePreferencesAndResetsLiveCapture()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        await PublishSnapshotAsync(CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));

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

    [AvaloniaFact]
    public async Task ResetCommand_ClearsCaptureButPreservesSidebarState()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        await PublishSnapshotAsync(CreateSnapshot(
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

    [AvaloniaFact]
    public async Task DampingSpeedCutoffCommit_PersistsThroughBikeCoordinator()
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
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
                owner.BaselineUpdated,
                SuspensionType.Front,
                DampingSpeedCircuit.Compression,
                260)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Saved(savedBike));
        var context = CreateSessionContext(
            bikeId: bikeId,
            dampingSpeedCutoffs: initialCutoffs,
            dampingSpeedCutoffOwner: owner);
        var editor = CreateEditor(context, bikeCoordinator);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        await bikeCoordinator.Received(1).UpdateDampingSpeedCutoffAsync(
            bikeId,
            owner.BaselineUpdated,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            260);
        Assert.Equal(savedCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(savedCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.True(editor.CanEditDampingSpeedCutoffs);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task DampingSpeedCutoffCommitFailure_RestoresPersistedCutoffsAndReportsError()
    {
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId,
                owner.BaselineUpdated,
                SuspensionType.Rear,
                DampingSpeedCircuit.Rebound,
                600)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Failed("disk full"));
        var context = CreateSessionContext(
            bikeId: bikeId,
            dampingSpeedCutoffs: initialCutoffs,
            dampingSpeedCutoffOwner: owner);
        var editor = CreateEditor(context, bikeCoordinator);
        editor.PreviewDampingSpeedCutoff(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 600);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 600);

        Assert.Equal(initialCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(initialCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.Contains(editor.ErrorMessages, message => message.Contains("disk full", StringComparison.Ordinal));
    }

    [AvaloniaFact]
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

        await PublishSnapshotAsync(CreateSnapshot(canSave: true, telemetryData: TestTelemetryData.CreateProcessed()));
        await firstBakeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondBakeApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.SpringPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SpringPageViewModel.FrontTravelDistribution) &&
                editor.SpringPage.FrontTravelDistribution == secondData.FrontTravelDistribution)
            {
                secondBakeApplied.TrySetResult();
            }
        };

        await PublishSnapshotAsync(CreateSnapshot(
            canSave: true,
            telemetryData: TestTelemetryData.CreateProcessed(),
            captureRevision: 2));
        firstBakeGate.SetResult(firstData);
        await WaitForUiRefreshAsync();
        if (editor.SpringPage.FrontTravelDistribution != secondData.FrontTravelDistribution)
        {
            await secondBakeApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        IBikeCoordinator? bikeCoordinator = null)
    {
        return new LiveSessionDetailViewModel(
            context ?? CreateSessionContext(),
            liveSessionService,
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            new InlineUiThreadDispatcher(),
            bikeCoordinator);
    }

    private async Task PublishSnapshotAsync(LiveSessionPresentationSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        snapshots.OnNext(snapshot);
        await WaitForUiRefreshAsync();
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
            AnalysisTelemetry: telemetryData,
            DampingPercentages: new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8),
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
        bool hasRearTravelCalibration = true,
        Guid? bikeId = null,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null,
        DampingSpeedCutoffOwner? dampingSpeedCutoffOwner = null)
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
            DampingSpeedCutoffs: dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: dampingSpeedCutoffOwner ?? new DampingSpeedCutoffOwner(resolvedBikeId, 0));
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

    private async Task WaitForUiRefreshAsync()
    {
        if (uiTimers.HasScheduledTimers)
        {
            uiTimers.FireAll();
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
