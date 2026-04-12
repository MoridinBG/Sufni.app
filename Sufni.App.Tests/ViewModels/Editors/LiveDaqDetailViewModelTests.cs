using System.Reactive.Subjects;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LiveDaqDetailViewModelTests
{
    private readonly ILiveDaqClientFactory clientFactory = Substitute.For<ILiveDaqClientFactory>();
    private readonly ILiveDaqClient client = Substitute.For<ILiveDaqClient>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly Subject<LiveDaqClientEvent> clientEvents = new();

    public LiveDaqDetailViewModelTests()
    {
        clientFactory.CreateClient().Returns(client);
        client.Events.Returns(clientEvents);
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        client.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
    }

    private LiveDaqDetailViewModel CreateEditor(LivePreviewStartResult? startResult = null)
    {
        var result = startResult ?? new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808),
            LiveSensorMask.Travel | LiveSensorMask.Imu);

        client.StartPreviewAsync(Arg.Any<LiveStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                EmitStartedEvents(clientEvents, result);
                return Task.FromResult(result);
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
            clientFactory,
            shell,
            dialogService);
    }

    [AvaloniaFact]
    public async Task Loaded_AutoConnects_AndAppliesAcceptedSessionState()
    {
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808);
        var editor = CreateEditor(new LivePreviewStartResult.Started(sessionHeader, LiveSensorMask.Travel | LiveSensorMask.Imu));

        await editor.LoadedCommand.ExecuteAsync(null);

        await client.Received(1).ConnectAsync("192.168.0.50", 1557, Arg.Any<CancellationToken>());
        await client.Received(1).StartPreviewAsync(Arg.Any<LiveStartRequest>(), Arg.Any<CancellationToken>());
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
        await client.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        Assert.Equal(LiveConnectionState.Disconnected, editor.Snapshot.ConnectionState);
    }

    [AvaloniaFact]
    public async Task Unloaded_DisconnectsClient()
    {
        var disconnectCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            disconnectCalled.TrySetResult();
            return Task.CompletedTask;
        });

        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        editor.UnloadedCommand.Execute(null);
        await disconnectCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await client.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SeparateEditors_UseIndependentClients_AndUnloadOnlyDisconnectsClosingTab()
    {
        var client1 = Substitute.For<ILiveDaqClient>();
        var client2 = Substitute.For<ILiveDaqClient>();
        var client1Events = new Subject<LiveDaqClientEvent>();
        var client2Events = new Subject<LiveDaqClientEvent>();
        var factory = Substitute.For<ILiveDaqClientFactory>();
        factory.CreateClient().Returns(client1, client2);

        client1.Events.Returns(client1Events);
        client2.Events.Returns(client2Events);
        client1.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        client2.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        client1.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        client2.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var result1 = new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 101),
            LiveSensorMask.Travel | LiveSensorMask.Imu);
        var result2 = new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 202),
            LiveSensorMask.Travel | LiveSensorMask.Imu);
        client1.StartPreviewAsync(Arg.Any<LiveStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                EmitStartedEvents(client1Events, result1);
                return Task.FromResult<LivePreviewStartResult>(result1);
            });
        client2.StartPreviewAsync(Arg.Any<LiveStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                EmitStartedEvents(client2Events, result2);
                return Task.FromResult<LivePreviewStartResult>(result2);
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

        var editor1 = new LiveDaqDetailViewModel(snapshot1, factory, shell, dialogService);
        var editor2 = new LiveDaqDetailViewModel(snapshot2, factory, shell, dialogService);

        await editor1.LoadedCommand.ExecuteAsync(null);
        await editor2.LoadedCommand.ExecuteAsync(null);

        editor1.UnloadedCommand.Execute(null);

        await client1.Received(1).ConnectAsync("192.168.0.50", 1557, Arg.Any<CancellationToken>());
        await client2.Received(1).ConnectAsync("192.168.0.51", 1666, Arg.Any<CancellationToken>());
        await client1.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        await client2.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
        Assert.Equal((uint)101, editor1.Snapshot.Session.SessionId);
        Assert.Equal((uint)202, editor2.Snapshot.Session.SessionId);
    }

    private static void EmitStartedEvents(Subject<LiveDaqClientEvent> events, LivePreviewStartResult result)
    {
        if (result is not LivePreviewStartResult.Started started)
        {
            return;
        }

        var ackHeader = new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.StartLiveAck, 0, 0);
        events.OnNext(new LiveDaqClientEvent.FrameReceived(
            new LiveStartAckFrame(ackHeader, new LiveStartAck(LiveStartErrorCode.Ok, started.Header.SessionId, started.SelectedSensorMask))));

        var sessionHeader = new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.SessionHeader, 0, 0);
        events.OnNext(new LiveDaqClientEvent.FrameReceived(
            new LiveSessionHeaderFrame(sessionHeader, started.Header)));
    }
}