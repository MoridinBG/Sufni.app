using System.Reactive.Subjects;
using System.Text;
using NSubstitute;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal sealed class LiveDaqDetailHarness
{
    private LiveDaqSharedStreamState currentStreamState = LiveDaqSharedStreamState.Empty;
    private LiveDaqSnapshot currentCatalogSnapshot = DefaultSnapshot();
    private LiveDaqStreamConfiguration currentConfiguration = LiveDaqStreamConfiguration.Default;

    public LiveDaqDetailHarness()
    {
        DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        DialogService.ShowContentDialogAsync(Arg.Any<object>(), Arg.Any<DialogOptions>())
            .Returns(Task.FromResult(PromptResult.Ok));
        KnownBoardsQuery.Changes.Returns(KnownBoardsChanges);
        KnownBoardsQuery.GetSessionContext("board-1").Returns(CreateSessionContext("board-1"));
        FilesService.OpenDeviceConfigFileAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SelectedDeviceConfigFile?>(null));
        DaqManagementService.SetTimeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqSetTimeResult>(new DaqSetTimeResult.Ok(TimeSpan.FromMilliseconds(30))));
        DaqManagementService.ReplaceConfigAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqManagementResult>(new DaqManagementResult.Ok()));
        DaqManagementService.GetFileAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DaqFileClass>(),
                Arg.Any<int>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var bytes = Encoding.UTF8.GetBytes("STA_SSID=trail\n");
                callInfo.ArgAt<Stream>(4).Write(bytes);
                return Task.FromResult<DaqGetFileResult>(new DaqGetFileResult.Downloaded("CONFIG", (ulong)bytes.Length));
            });
        SharedStream.Frames.Returns(Frames);
        SharedStream.States.Returns(StreamStates);
        SharedStream.CurrentState.Returns(_ => currentStreamState);
        SharedStream.CatalogSnapshot.Returns(_ => currentCatalogSnapshot);
        SharedStream.RequestedConfiguration.Returns(_ => currentConfiguration);
        SharedStream.AcquireLease().Returns(StreamLease);
        SharedStream.ApplyConfigurationAsync(Arg.Any<LiveDaqStreamConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                currentConfiguration = callInfo.ArgAt<LiveDaqStreamConfiguration>(0);
                return Task.CompletedTask;
            });
        SharedStream.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        StreamLease.DisposeAsync().Returns(ValueTask.CompletedTask);
    }

    public ILiveDaqSharedStream SharedStream { get; } = Substitute.For<ILiveDaqSharedStream>();
    public ILiveDaqCoordinator LiveDaqCoordinator { get; } = TestCoordinatorSubstitutes.LiveDaq();
    public IDaqManagementService DaqManagementService { get; } = Substitute.For<IDaqManagementService>();
    public IFilesService FilesService { get; } = Substitute.For<IFilesService>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ILiveDaqKnownBoardsQuery KnownBoardsQuery { get; } = Substitute.For<ILiveDaqKnownBoardsQuery>();
    public LiveDaqStore LiveDaqStore { get; } = new();
    public Subject<LiveProtocolFrame> Frames { get; } = new();
    public BehaviorSubject<LiveDaqSharedStreamState> StreamStates { get; } = new(LiveDaqSharedStreamState.Empty);
    public BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> KnownBoardsChanges { get; } = new([]);
    public ILiveDaqSharedStreamLease StreamLease { get; } = Substitute.For<ILiveDaqSharedStreamLease>();

    public LiveDaqDetailViewModel CreateEditor(
        LivePreviewStartResult? startResult = null,
        LiveProtocolVersion protocolVersion = LiveProtocolVersion.V2)
    {
        currentCatalogSnapshot = DefaultSnapshot() with { ProtocolVersion = protocolVersion };
        var result = startResult ?? new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808));
        SharedStream.EnsureStartedAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                PublishStartResult(result);
                return Task.FromResult<LivePreviewStartResult?>(result);
            });

        return new LiveDaqDetailViewModel(
            currentCatalogSnapshot,
            SharedStream,
            LiveDaqCoordinator,
            DaqManagementService,
            FilesService,
            Shell,
            DialogService,
            KnownBoardsQuery,
            LiveDaqStore,
            new InlineUiThreadDispatcher());
    }

    public void PublishStreamState(LiveDaqSharedStreamState state)
    {
        currentStreamState = state;
        StreamStates.OnNext(state);
    }

    public static LiveDaqSnapshot DefaultSnapshot() => new(
        IdentityKey: "board-1",
        DisplayName: "Board 1",
        BoardId: "board-1",
        Host: "192.168.0.50",
        Port: 1557,
        IsOnline: true,
        SetupName: "race",
        BikeName: "demo");

    public static LiveDaqSessionContext CreateSessionContext(string identityKey) => new(
        IdentityKey: identityKey,
        BoardId: Guid.NewGuid(),
        DisplayName: "Board 1",
        SetupId: Guid.NewGuid(),
        SetupName: "race",
        BikeId: Guid.NewGuid(),
        BikeName: "demo",
        BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
        TravelCalibration: new LiveDaqTravelCalibration(null, null),
        DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
        DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(Guid.Empty, 0));

    private void PublishStartResult(LivePreviewStartResult result)
    {
        if (result is LivePreviewStartResult.Started started)
        {
            PublishStreamState(currentStreamState with
            {
                ConnectionState = LiveConnectionState.Connected,
                LastError = null,
                SessionHeader = started.Header,
                SelectedStreamMask = started.Header.AcceptedStreamMask,
            });
            return;
        }

        if (result is LivePreviewStartResult.Rejected rejected)
        {
            PublishStreamState(currentStreamState with
            {
                ConnectionState = LiveConnectionState.Disconnected,
                LastError = rejected.UserMessage,
                SessionHeader = null,
                SelectedStreamMask = LiveStreamMask.None,
            });
        }
    }
}
