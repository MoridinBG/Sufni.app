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
    public async Task ConnectFailure_RendersSafeSummaryAndDiagnosticDetails()
    {
        using var harness = new LiveDaqDetailHarness();
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
        using var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Unavailable(LiveStartUnavailableReason.Offline));

        await editor.ConnectCommand.ExecuteAsync(null);

        Assert.Single(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
    }

    [Fact]
    public async Task ManagementFailure_RendersSafeSummaryAndSeparateDiagnostics()
    {
        using var harness = new LiveDaqDetailHarness();
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
        using var harness = new LiveDaqDetailHarness();
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
    public async Task ConnectCommand_ReportsPortHolderWithoutTreatingBusyAsFailure()
    {
        using var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor(new LivePreviewStartResult.Busy(new DaqTransportHolder.Import(Guid.NewGuid())));

        await editor.ConnectCommand.ExecuteAsync(null);

        Assert.Single(editor.Notifications);
        Assert.Empty(editor.ErrorMessages);
    }

    [Fact]
    public async Task UploadConfigCommand_UploadsSelectedConfigAndClearsPendingState()
    {
        using var harness = new LiveDaqDetailHarness();
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
    public async Task ManagementCommands_DoNotRun_WhenCatalogAvailabilityBecomesOffline()
    {
        using var harness = new LiveDaqDetailHarness();
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
