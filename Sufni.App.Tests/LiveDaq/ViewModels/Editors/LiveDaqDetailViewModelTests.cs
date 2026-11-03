using System.Collections.Immutable;
using Sufni.App.Shared.Views.Dialogs;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Coordinators;
using System.Text;
using NSubstitute;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.LiveDaq;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

public class LiveDaqDetailViewModelTests
{
    [Fact]
    public async Task Availability_FollowsConnectionLockAndCurrentSetup()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        editor.SetTabActive(true);
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.True(editor.CanConnect);
        Assert.False(editor.StartSessionCommand.CanExecute(null));

        var connected = LiveDaqSharedStreamState.Empty with
        {
            ConnectionState = new LiveConnectionState.Connected(),
            SessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(),
            IsConfigurationLocked = true
        };
        harness.PublishStreamState(connected);
        Assert.False(editor.CanConnect);
        Assert.False(editor.CanDisconnect);
        Assert.False(editor.AreRequestedRatesEnabled);
        Assert.True(editor.StartSessionCommand.CanExecute(null));

        harness.PublishStreamState(connected with { IsConfigurationLocked = false });
        Assert.True(editor.CanDisconnect);
        Assert.True(editor.AreRequestedRatesEnabled);
        var commandChanged = false;
        editor.StartSessionCommand.CanExecuteChanged += (_, _) => commandChanged = true;
        harness.KnownBoardsQuery.GetSessionContext("board-1").Returns((Sufni.App.LiveDaq.Queries.LiveDaqSessionContext?)null);
        harness.KnownBoardsChanges.OnNext([]);
        Assert.True(commandChanged);
        Assert.False(editor.CanStartSession);
        Assert.False(editor.StartSessionCommand.CanExecute(null));

        harness.PublishStreamState(connected with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.Offline) });
        Assert.False(editor.CanConnect);
        Assert.False(editor.CanDisconnect);
        Assert.False(editor.AreRequestedRatesEnabled);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task ConnectFailure_RendersSafeSummaryAndDiagnosticDetails()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Failed(LiveStartFailureReason.Connection, "private socket detail"));

        await editor.ConnectCommand.ExecuteAsync(null);

        var failure = Assert.Single(editor.ErrorMessages);
        Assert.DoesNotContain("private socket detail", failure.Summary);
        Assert.Equal("private socket detail", failure.Details);
        Assert.DoesNotContain("private socket detail", editor.Snapshot.LastError!);
    }

    [Fact]
    public async Task ConnectUnavailable_ShowsNotificationWithoutFailure()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Unavailable(LiveStartUnavailableReason.Offline));

        await editor.ConnectCommand.ExecuteAsync(null);

        Assert.Single(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
    }

    [Fact]
    public async Task ManagementFailure_RendersSafeSummaryAndSeparateDiagnostics()
    {
        var harness = new LiveDaqDetailHarness();
        harness.LiveDaqCoordinator.SetTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LiveDaqManagementResult.Failed(Sufni.App.Shared.Models.DaqManagementFailureReason.SetTime, "socket diagnostic"));
        var editor = harness.CreateEditor();

        await editor.SetTimeCommand.ExecuteAsync(null);

        var failure = Assert.Single(editor.ErrorMessages);
        Assert.Equal("socket diagnostic", failure.Details);
        Assert.DoesNotContain("socket diagnostic", failure.Summary);
        Assert.False(editor.IsManagementBusy);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task PortOwnership_DisablesManagementAndConnect_WithoutChangingDiscovery()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        Assert.True(editor.CanManage);
        Assert.True(editor.CanConnect);

        harness.ManagementAvailability.OnNext(harness.ManagementAvailability.Value with
        {
            Holder = new DaqTransportHolder.Import(Guid.NewGuid())
        });

        Assert.False(editor.CanManage);
        Assert.False(editor.CanConnect);
        Assert.NotNull(editor.ManagementDisabledTooltip);
        harness.ManagementAvailability.OnNext(harness.ManagementAvailability.Value with { Holder = null });
        Assert.True(editor.CanManage);
        Assert.True(editor.CanConnect);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public async Task UnloadedCommand_RetainsObserverLease_UntilTabCloses()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        harness.SharedStream.Received(1).AcquireLease();
        await editor.LoadedCommand.ExecuteAsync(null);

        await editor.UnloadedCommand.ExecuteAsync(null);
        await harness.StreamLease.DidNotReceive().DisposeAsync();
        await harness.SharedStream.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
        await editor.LoadedCommand.ExecuteAsync(null);
        harness.SharedStream.Received(1).AcquireLease();
        await editor.PrepareCloseAsync();

        await harness.StreamLease.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ConnectCommand_ReportsPortHolderWithoutTreatingBusyAsFailure()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Busy(new DaqTransportHolder.Import(Guid.NewGuid())));

        await editor.ConnectCommand.ExecuteAsync(null);

        Assert.Single(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
    }

    [Fact]
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
        await harness.SharedStream.Received(1).ConnectLiveAsync(Arg.Any<CancellationToken>());
        Assert.Equal(new LiveConnectionState.Connected(), editor.Snapshot.ConnectionState);
        Assert.Equal((uint)808, editor.Snapshot.Session.SessionId);
        Assert.Equal("Board 1", editor.Name);
        Assert.Equal("192.168.0.50:1557", editor.Endpoint);
    }

    [Fact]
    public async Task ConnectCommand_PushesErrorMessage_WhenStartIsRejected()
    {
        const string rejectionMessage = "start rejected sentinel";
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Rejected(LiveStartErrorCode.Busy, rejectionMessage));

        await editor.ConnectCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
        Assert.IsType<LivePreviewStartResult.Rejected>(Assert.IsType<LiveConnectionState.Disconnected>(editor.Snapshot.ConnectionState).Outcome);
    }

    [Fact]
    public async Task SetTimeCommand_AddsNotification_OnSuccess()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();

        await editor.SetTimeCommand.ExecuteAsync(null);

        await harness.LiveDaqCoordinator.Received(1)
            .SetTimeAsync("board-1", Arg.Any<CancellationToken>());
        Assert.Single(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
        Assert.False(editor.IsManagementBusy);
    }

    [Fact]
    public async Task UploadConfigCommand_UploadsSelectedConfigAndClearsPendingState()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        await StageConfigAsync(harness, editor, new SelectedDeviceConfigFile("CONFIG", [1, 2, 3]));

        await editor.UploadConfigCommand.ExecuteAsync(null);

        await harness.LiveDaqCoordinator.Received(1).WriteConfigAsync(
            "board-1",
            Arg.Is<ReadOnlyMemory<byte>>(bytes => bytes.ToArray().SequenceEqual(new byte[] { 1, 2, 3 })),
            Arg.Any<CancellationToken>());
        Assert.False(editor.HasPendingConfig);
        Assert.Null(editor.PendingConfigFileName);
        Assert.Single(editor.Notifications);
    }

    [Fact]
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

        await harness.LiveDaqCoordinator.Received(1).ReadConfigAsync("board-1", Arg.Any<CancellationToken>());
        await harness.DialogService.Received(1).ShowContentDialogAsync(
            Arg.Is<object>(content => content is LiveDaqConfigEditorViewModel),
            Arg.Any<DialogOptions>());
        var configEditor = Assert.IsType<LiveDaqConfigEditorViewModel>(dialogContent);
        configEditor.Fields.Single(row => row.Key == "STA_SSID").Value = "edited";

        await configEditor.SaveCommand.ExecuteAsync(null);

        await harness.LiveDaqCoordinator.Received(1).SaveEditedConfigAsync(
            "board-1", Arg.Any<DaqConfigDocument>(),
            Arg.Is<ImmutableDictionary<string, string>>(values => values["STA_SSID"] == "edited"),
            Arg.Any<CancellationToken>());
        Assert.Single(editor.Notifications);
        Assert.False(editor.IsManagementBusy);
    }

    [Fact]
    public async Task LoadedCommand_DoesNotStartForegroundUpdates_WhenTabIsInactive()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.False(harness.UiTimers.HasScheduledTimers);
        Assert.False(harness.Frames.HasObservers);
        Assert.False(harness.StreamStates.HasObservers);
        _ = harness.SharedStream.Received(1).AcquireLease();
        await harness.StreamLease.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task TabDeactivation_StopsForegroundUpdates_ButRetainsStreamLease()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        editor.SetTabActive(true);
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.True(harness.UiTimers.HasScheduledTimers);
        Assert.True(harness.Frames.HasObservers);
        Assert.True(harness.StreamStates.HasObservers);

        editor.SetTabActive(false);

        Assert.False(harness.UiTimers.HasScheduledTimers);
        Assert.False(harness.Frames.HasObservers);
        Assert.False(harness.StreamStates.HasObservers);
        await harness.StreamLease.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task ReactivatingClosedTab_DoesNotSubscribeToDisposedSharedStream()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);
        editor.SetTabActive(true);
        editor.SetTabActive(false);
        harness.PublishStreamState(LiveDaqSharedStreamState.Empty with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.Offline) });
        harness.Frames.Dispose();
        harness.StreamStates.Dispose();

        var exception = Record.Exception(() => editor.SetTabActive(true));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ManagementCommands_DoNotRun_WhenCatalogAvailabilityBecomesOffline()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        editor.SetTabActive(true);
        await editor.LoadedCommand.ExecuteAsync(null);

        harness.LiveDaqStore.ReplaceAll([LiveDaqDetailHarness.DefaultSnapshot()]);
        await Task.Yield();

        Assert.True(editor.CanManage);

        harness.PublishStreamState(LiveDaqSharedStreamState.Empty with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.Offline) });
        harness.LiveDaqStore.ReplaceAll([]);
        harness.ManagementAvailability.OnNext(new(null, null));
        await Task.Yield();

        Assert.False(editor.CanManage);

        await editor.SetTimeCommand.ExecuteAsync(null);

        await harness.LiveDaqCoordinator.DidNotReceive()
            .SetTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
    }

    private static async Task StageConfigAsync(
        LiveDaqDetailHarness harness,
        LiveDaqDetailViewModel editor,
        SelectedDeviceConfigFile file)
    {
        harness.LiveDaqCoordinator.SelectConfigFileAsync(Arg.Any<CancellationToken>())
            .Returns(new LiveDaqManagementResult.ConfigSelected(file));

        await editor.SelectConfigFileCommand.ExecuteAsync(null);
    }
}
