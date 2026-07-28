using System.Text;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.LiveDaq;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

[Collection("Ui")]
public class LiveDaqDetailViewModelTests
{
    [AvaloniaFact]
    public async Task ConnectCommand_AppliesConfigurationAndAcceptedSessionState()
    {
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808);
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Started(sessionHeader));
        editor.RequestedTravelHz = 0;
        editor.RequestedImuHz = 200;
        editor.RequestedGpsFixHz = 0;

        await editor.ConnectCommand.ExecuteAsync(null);

        await harness.SharedStream.Received(1).ApplyConfigurationAsync(
            Arg.Is<LiveDaqStreamConfiguration>(configuration =>
                configuration.RequestedSensorMask == LiveSensorInstanceMask.Imu
                && configuration.TravelHz == 0
                && configuration.ImuHz == 200
                && configuration.GpsFixHz == 0),
            Arg.Any<CancellationToken>());
        await harness.SharedStream.Received(1).EnsureStartedAsync(Arg.Any<CancellationToken>());
        Assert.Equal(LiveConnectionState.Connected, editor.Snapshot.ConnectionState);
        Assert.Equal((uint)808, editor.Snapshot.Session.SessionId);
        Assert.Equal("Board 1", editor.Name);
        Assert.Equal("192.168.0.50:1557", editor.Endpoint);
    }

    [AvaloniaFact]
    public async Task ConnectCommand_PushesErrorMessage_WhenStartIsRejected()
    {
        const string rejectionMessage = "start rejected sentinel";
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Rejected(LiveStartErrorCode.Busy, rejectionMessage));

        await editor.ConnectCommand.ExecuteAsync(null);

        Assert.Contains(rejectionMessage, editor.ErrorMessages);
        Assert.Equal(LiveConnectionState.Disconnected, editor.Snapshot.ConnectionState);
    }

    [AvaloniaFact]
    public async Task SetTimeCommand_AddsNotification_OnSuccess()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();

        await editor.SetTimeCommand.ExecuteAsync(null);

        await harness.DaqManagementService.Received(1)
            .SetTimeAsync("192.168.0.50", 1557, Arg.Any<CancellationToken>());
        Assert.Single(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
        Assert.False(editor.IsManagementBusy);
    }

    [AvaloniaFact]
    public async Task UploadConfigCommand_UploadsSelectedConfigAndClearsPendingState()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        await StageConfigAsync(harness, editor, new SelectedDeviceConfigFile("CONFIG", [1, 2, 3]));

        await editor.UploadConfigCommand.ExecuteAsync(null);

        await harness.DaqManagementService.Received(1).ReplaceConfigAsync(
            "192.168.0.50",
            1557,
            Arg.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 1, 2, 3 })),
            Arg.Any<CancellationToken>());
        Assert.False(editor.HasPendingConfig);
        Assert.Null(editor.PendingConfigFileName);
        Assert.Single(editor.Notifications);
    }

    [AvaloniaFact]
    public async Task EditConfigCommand_DownloadsConfigAndUploadsDialogChanges()
    {
        object? dialogContent = null;
        var harness = new LiveDaqDetailHarness();
        harness.DialogService.ShowContentDialogAsync(
                Arg.Do<object>(content => dialogContent = content),
                Arg.Any<DialogOptions>())
            .Returns(Task.FromResult(PromptResult.Ok));
        var editor = harness.CreateEditor();

        await editor.EditConfigCommand.ExecuteAsync(null);

        await harness.DaqManagementService.Received(1).GetFileAsync(
            "192.168.0.50",
            1557,
            DaqFileClass.Config,
            0,
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
        await harness.DialogService.Received(1).ShowContentDialogAsync(
            Arg.Is<object>(content => content is LiveDaqConfigEditorViewModel),
            Arg.Any<DialogOptions>());
        var configEditor = Assert.IsType<LiveDaqConfigEditorViewModel>(dialogContent);
        configEditor.Fields.Single(row => row.Key == "STA_SSID").Value = "edited";

        await configEditor.SaveCommand.ExecuteAsync(null);

        await harness.DaqManagementService.Received(1).ReplaceConfigAsync(
            "192.168.0.50",
            1557,
            Arg.Is<byte[]>(bytes => Encoding.UTF8.GetString(bytes).Contains("STA_SSID=edited", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        Assert.Single(editor.Notifications);
        Assert.False(editor.IsManagementBusy);
    }

    [AvaloniaFact]
    public async Task LoadedCommand_DoesNotStartForegroundUpdates_WhenTabIsInactive()
    {
        using var uiTimers = ManualPeriodicUiTimerScheduler.Install();
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.False(uiTimers.HasScheduledTimers);
        Assert.False(harness.Frames.HasObservers);
        Assert.False(harness.StreamStates.HasObservers);
        _ = harness.SharedStream.Received(1).AcquireLease();
        await harness.StreamLease.DidNotReceive().DisposeAsync();
    }

    [AvaloniaFact]
    public async Task TabDeactivation_StopsForegroundUpdates_ButRetainsStreamLease()
    {
        using var uiTimers = ManualPeriodicUiTimerScheduler.Install();
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        editor.SetTabActive(true);
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.True(uiTimers.HasScheduledTimers);
        Assert.True(harness.Frames.HasObservers);
        Assert.True(harness.StreamStates.HasObservers);

        editor.SetTabActive(false);

        Assert.False(uiTimers.HasScheduledTimers);
        Assert.False(harness.Frames.HasObservers);
        Assert.False(harness.StreamStates.HasObservers);
        await harness.StreamLease.DidNotReceive().DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ReactivatingClosedTab_DoesNotSubscribeToDisposedSharedStream()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        editor.SetTabActive(true);
        editor.SetTabActive(false);
        harness.PublishStreamState(LiveDaqSharedStreamState.Empty with { IsClosed = true });
        harness.Frames.Dispose();
        harness.StreamStates.Dispose();

        var exception = Record.Exception(() => editor.SetTabActive(true));

        Assert.Null(exception);
    }

    [AvaloniaFact]
    public async Task ManagementCommands_DoNotRun_AfterStoreRemovesRow()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        editor.SetTabActive(true);
        await editor.LoadedCommand.ExecuteAsync(null);

        harness.LiveDaqStore.Upsert(LiveDaqDetailHarness.DefaultSnapshot());
        await Task.Yield();

        Assert.True(editor.CanManage);

        harness.PublishStreamState(LiveDaqSharedStreamState.Empty with { IsClosed = true });
        harness.LiveDaqStore.Remove("board-1");
        await Task.Yield();

        Assert.False(editor.CanManage);

        await editor.SetTimeCommand.ExecuteAsync(null);

        await harness.DaqManagementService.DidNotReceive()
            .SetTimeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Empty(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
    }

    private static async Task StageConfigAsync(
        LiveDaqDetailHarness harness,
        LiveDaqDetailViewModel editor,
        SelectedDeviceConfigFile file)
    {
        harness.FilesService.OpenDeviceConfigFileAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SelectedDeviceConfigFile?>(file));

        await editor.SelectConfigFileCommand.ExecuteAsync(null);
    }
}
