using System.Reactive.Subjects;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LiveDaqDetailViewModelTests
{
    private readonly ILiveDaqSharedStream sharedStream = Substitute.For<ILiveDaqSharedStream>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery = Substitute.For<ILiveDaqKnownBoardsQuery>();
    private readonly Subject<LiveProtocolFrame> frames = new();
    private readonly BehaviorSubject<LiveDaqSharedStreamState> streamStates = new(LiveDaqSharedStreamState.Empty);
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> knownBoardsChanges = new([]);
    private LiveDaqSharedStreamState currentStreamState = LiveDaqSharedStreamState.Empty;
    private LiveDaqStreamConfiguration currentConfiguration = LiveDaqStreamConfiguration.Default;

    public LiveDaqDetailViewModelTests()
    {
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        knownBoardsQuery.Changes.Returns(knownBoardsChanges);
        sharedStream.Frames.Returns(frames);
        sharedStream.States.Returns(streamStates);
        sharedStream.CurrentState.Returns(_ => currentStreamState);
        sharedStream.RequestedConfiguration.Returns(_ => currentConfiguration);
        sharedStream.AttachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        sharedStream.DetachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        sharedStream.ApplyConfigurationAsync(Arg.Any<LiveDaqStreamConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                currentConfiguration = callInfo.ArgAt<LiveDaqStreamConfiguration>(0);
                return Task.CompletedTask;
            });
        sharedStream.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private LiveDaqDetailViewModel CreateEditor(LivePreviewStartResult? startResult = null)
    {
        var result = startResult ?? new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808),
            LiveSensorMask.Travel | LiveSensorMask.Imu);

        sharedStream.EnsureStartedAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (result is LivePreviewStartResult.Started started)
                {
                    currentStreamState = currentStreamState with
                    {
                        ConnectionState = LiveConnectionState.Connected,
                        LastError = null,
                        SessionHeader = started.Header,
                        SelectedSensorMask = started.SelectedSensorMask,
                    };
                    streamStates.OnNext(currentStreamState);
                }
                else if (result is LivePreviewStartResult.Rejected rejected)
                {
                    currentStreamState = currentStreamState with
                    {
                        ConnectionState = LiveConnectionState.Disconnected,
                        LastError = rejected.UserMessage,
                        SessionHeader = null,
                        SelectedSensorMask = LiveSensorMask.None,
                    };
                    streamStates.OnNext(currentStreamState);
                }

                return Task.FromResult<LivePreviewStartResult?>(result);
            });

        return new LiveDaqDetailViewModel(
            new LiveDaqSnapshot(
                IdentityKey: "board-1",
                DisplayName: "Board 1",
                BoardId: "board-1",
                Host: "192.168.0.50",
                Port: 1557,
                IsOnline: true,
                SetupName: "race",
                BikeName: "demo"),
            sharedStream,
            shell,
            dialogService,
            knownBoardsQuery);
    }

    [AvaloniaFact]
    public async Task Loaded_AutoConnects_AndAppliesAcceptedSessionState()
    {
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808);
        var editor = CreateEditor(new LivePreviewStartResult.Started(sessionHeader, LiveSensorMask.Travel | LiveSensorMask.Imu));

        await editor.LoadedCommand.ExecuteAsync(null);

        await sharedStream.Received(1).AttachDiagnosticsAsync(Arg.Any<CancellationToken>());
        await sharedStream.Received(1).ApplyConfigurationAsync(Arg.Any<LiveDaqStreamConfiguration>(), Arg.Any<CancellationToken>());
        await sharedStream.Received(1).EnsureStartedAsync(Arg.Any<CancellationToken>());
        Assert.Equal(LiveConnectionState.Connected, editor.Snapshot.ConnectionState);
        Assert.Equal((uint)808, editor.Snapshot.Session.SessionId);
        Assert.Equal("Board 1", editor.Name);
        Assert.Equal("192.168.0.50:1557", editor.Endpoint);
    }

    [AvaloniaFact]
    public async Task Loaded_ShowsDialogAndClosesTab_WhenStartIsRejected()
    {
        var editor = CreateEditor(new LivePreviewStartResult.Rejected(LiveStartErrorCode.Busy, "busy"));

        await editor.LoadedCommand.ExecuteAsync(null);

        await dialogService.Received(1).ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        shell.Received(1).Close(editor);
        Assert.Equal(LiveConnectionState.Disconnected, editor.Snapshot.ConnectionState);
    }

    [AvaloniaFact]
    public async Task Unloaded_DetachesDiagnosticsStream()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        await editor.UnloadedCommand.ExecuteAsync(null);

        await sharedStream.Received(1).DetachDiagnosticsAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task CloseCommand_WaitsForDetach_BeforeClosingTab()
    {
        var detachGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sharedStream.DetachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(_ => detachGate.Task);

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        var closeTask = editor.CloseCommand.ExecuteAsync(null);

        Assert.False(closeTask.IsCompleted);
        shell.DidNotReceive().Close(editor);

        detachGate.TrySetResult();
        await closeTask;

        await sharedStream.Received(1).DetachDiagnosticsAsync(Arg.Any<CancellationToken>());
        shell.Received(1).Close(editor);
    }

    [AvaloniaFact]
    public async Task SeparateEditors_UseIndependentStreams_AndUnloadOnlyDetachesClosingTab()
    {
        var stream1 = Substitute.For<ILiveDaqSharedStream>();
        var stream2 = Substitute.For<ILiveDaqSharedStream>();
        var query = Substitute.For<ILiveDaqKnownBoardsQuery>();
        query.Changes.Returns(new BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>>([]));
        var result1 = new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 101),
            LiveSensorMask.Travel | LiveSensorMask.Imu);
        var result2 = new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 202),
            LiveSensorMask.Travel | LiveSensorMask.Imu);
        var state1 = LiveDaqSharedStreamState.Empty;
        var state2 = LiveDaqSharedStreamState.Empty;
        var frames1 = new Subject<LiveProtocolFrame>();
        var frames2 = new Subject<LiveProtocolFrame>();
        var states1 = new BehaviorSubject<LiveDaqSharedStreamState>(state1);
        var states2 = new BehaviorSubject<LiveDaqSharedStreamState>(state2);

        stream1.Frames.Returns(frames1);
        stream2.Frames.Returns(frames2);
        stream1.States.Returns(states1);
        stream2.States.Returns(states2);
        stream1.CurrentState.Returns(_ => state1);
        stream2.CurrentState.Returns(_ => state2);
        stream1.RequestedConfiguration.Returns(LiveDaqStreamConfiguration.Default);
        stream2.RequestedConfiguration.Returns(LiveDaqStreamConfiguration.Default);
        stream1.AttachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        stream2.AttachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        stream1.DetachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        stream2.DetachDiagnosticsAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        stream1.ApplyConfigurationAsync(Arg.Any<LiveDaqStreamConfiguration>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        stream2.ApplyConfigurationAsync(Arg.Any<LiveDaqStreamConfiguration>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        stream1.EnsureStartedAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                state1 = state1 with
                {
                    ConnectionState = LiveConnectionState.Connected,
                    SessionHeader = ((LivePreviewStartResult.Started)result1).Header,
                    SelectedSensorMask = ((LivePreviewStartResult.Started)result1).SelectedSensorMask,
                };
                states1.OnNext(state1);
                return Task.FromResult<LivePreviewStartResult?>(result1);
            });
        stream2.EnsureStartedAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                state2 = state2 with
                {
                    ConnectionState = LiveConnectionState.Connected,
                    SessionHeader = ((LivePreviewStartResult.Started)result2).Header,
                    SelectedSensorMask = ((LivePreviewStartResult.Started)result2).SelectedSensorMask,
                };
                states2.OnNext(state2);
                return Task.FromResult<LivePreviewStartResult?>(result2);
            });

        var snapshot1 = new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.50",
            Port: 1557,
            IsOnline: true,
            SetupName: "race",
            BikeName: "demo");
        var snapshot2 = new LiveDaqSnapshot(
            IdentityKey: "board-2",
            DisplayName: "Board 2",
            BoardId: "board-2",
            Host: "192.168.0.51",
            Port: 1666,
            IsOnline: true,
            SetupName: "park",
            BikeName: "demo");

        var editor1 = new LiveDaqDetailViewModel(snapshot1, stream1, shell, dialogService, query);
        var editor2 = new LiveDaqDetailViewModel(snapshot2, stream2, shell, dialogService, query);

        await editor1.LoadedCommand.ExecuteAsync(null);
        await editor2.LoadedCommand.ExecuteAsync(null);

        await editor1.UnloadedCommand.ExecuteAsync(null);

        await stream1.Received(1).EnsureStartedAsync(Arg.Any<CancellationToken>());
        await stream2.Received(1).EnsureStartedAsync(Arg.Any<CancellationToken>());
        await stream1.Received(1).DetachDiagnosticsAsync(Arg.Any<CancellationToken>());
        await stream2.DidNotReceive().DetachDiagnosticsAsync(Arg.Any<CancellationToken>());
        Assert.Equal((uint)101, editor1.Snapshot.Session.SessionId);
        Assert.Equal((uint)202, editor2.Snapshot.Session.SessionId);
    }

    [AvaloniaFact]
    public void SnapshotTexts_ShowCalibratedMillimeters_WhenQueryProvidesCalibration()
    {
        knownBoardsQuery.GetTravelCalibration("board-1").Returns(new LiveDaqTravelCalibration(
            Front: new LiveDaqTravelChannelCalibration(200, measurement => measurement * 0.5),
            Rear: null));

        var editor = CreateEditor();

        editor.Snapshot = LiveDaqUiSnapshot.Empty with
        {
            Travel = new LiveTravelUiSnapshot(
                IsActive: true,
                HasData: true,
                FrontMeasurement: 120,
                RearMeasurement: 222,
                SampleOffset: TimeSpan.FromSeconds(1.25),
                SampleDelay: TimeSpan.FromMilliseconds(42),
                QueueDepth: 3,
                DroppedBatches: 1)
        };

        Assert.Equal("Front: 60mm (30%)", editor.FrontTravelText);
        Assert.Equal("Rear: 222", editor.RearTravelText);
    }
}