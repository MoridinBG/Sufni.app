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
        client.StartPreviewAsync(Arg.Any<LiveStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(startResult ?? new LivePreviewStartResult.Started(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808))));

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
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808, sensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu);
        var editor = CreateEditor(new LivePreviewStartResult.Started(sessionHeader));

        await editor.LoadedCommand.ExecuteAsync(null);

        await client.Received(1).ConnectAsync("192.168.0.50", 1557, Arg.Any<CancellationToken>());
        await client.Received(1).StartPreviewAsync(
            Arg.Is<LiveStartRequest>(request =>
                request.SensorMask == (LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps) &&
                request.TravelHz == 0 &&
                request.ImuHz == 0 &&
                request.GpsFixHz == 0),
            Arg.Any<CancellationToken>());
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
}