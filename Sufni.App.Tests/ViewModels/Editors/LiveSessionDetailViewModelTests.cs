using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LiveSessionDetailViewModelTests
{
    private readonly ILiveSessionService liveSessionService = Substitute.For<ILiveSessionService>();
    private readonly ISessionCoordinator sessionCoordinator = Substitute.For<ISessionCoordinator>();
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
        sessionCoordinator.SaveLiveCaptureAsync(Arg.Any<Session>(), Arg.Any<LiveSessionCapturePackage>(), Arg.Any<CancellationToken>())
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
            Arg.Any<CancellationToken>());
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

    private LiveSessionDetailViewModel CreateEditor()
    {
        return new LiveSessionDetailViewModel(
            CreateSessionContext(),
            liveSessionService,
            sessionCoordinator,
            tileLayerService,
            shell,
            dialogService);
    }

    private static LiveSessionPresentationSnapshot CreateSnapshot(
        bool canSave,
        TelemetryData? telemetryData = null,
        IReadOnlyList<TrackPoint>? trackPoints = null)
    {
        var header = LiveProtocolTestFrames.CreateSessionHeaderModel();
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
                CanSave: canSave,
                IsSaving: false,
                IsResetting: false),
            CaptureRevision: canSave ? 1 : 0);
    }

    private static LiveDaqSessionContext CreateSessionContext()
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
            TravelCalibration: new LiveDaqTravelCalibration(null, null));
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
}