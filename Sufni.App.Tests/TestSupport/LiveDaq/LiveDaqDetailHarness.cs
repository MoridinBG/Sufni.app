using System.Collections.Immutable;
using Sufni.App.Shared.Views.Dialogs;
using Sufni.App.LiveDaq.Services;
using System.Reactive.Subjects;
using Sufni.App.Tests.TestSupport.Async;
using Microsoft.Extensions.Time.Testing;
using System.Text;
using NSubstitute;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Bikes.Models;
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

internal sealed class LiveDaqDetailHarness : IDisposable
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
        LiveDaqCoordinator.ObserveManagementAvailability(Arg.Any<string>()).Returns(ManagementAvailability);
        LiveDaqCoordinator.SelectConfigFileAsync(Arg.Any<CancellationToken>()).Returns(new LiveDaqManagementResult.Canceled());
        LiveDaqCoordinator.SetTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LiveDaqManagementResult.TimeSet(TimeSpan.FromMilliseconds(30)));
        LiveDaqCoordinator.WriteConfigAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new LiveDaqManagementResult.ConfigWritten());
        LiveDaqCoordinator.SaveEditedConfigAsync(Arg.Any<string>(), Arg.Any<DaqConfigDocument>(), Arg.Any<ImmutableDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new LiveDaqManagementResult.ConfigWritten());
        LiveDaqCoordinator.ReadConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LiveDaqManagementResult.ConfigRead(DaqConfigDocument.Parse(Encoding.UTF8.GetBytes("STA_SSID=trail\n"))));
        SharedStream.Frames.Returns(Frames);
        SharedStream.States.Returns(StreamStates);
        SharedStream.CurrentState.Returns(_ => currentStreamState);
        SharedStream.CatalogSnapshot.Returns(_ => currentCatalogSnapshot);
        SharedStream.RequestedConfiguration.Returns(_ => currentConfiguration);
        SharedStream.AcquireLease().Returns(new LiveStreamLeaseResult.Acquired(StreamLease));
        SharedStream.ApplyConfigurationAsync(Arg.Any<LiveDaqStreamConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                currentConfiguration = callInfo.ArgAt<LiveDaqStreamConfiguration>(0);
                return Task.FromResult<LivePreviewStartResult?>(null);
            });
        SharedStream.StopAsync(Arg.Any<CancellationToken>()).Returns(LiveDisconnectResult.Stopped);
        StreamLease.DisposeAsync().Returns(ValueTask.CompletedTask);
    }

    public FakeTimeProvider Time { get; } = new();
    public ManualUiTimerScheduler UiTimers { get; } = new();

    public ILiveDaqSharedStream SharedStream { get; } = Substitute.For<ILiveDaqSharedStream>();
    public ILiveDaqCoordinator LiveDaqCoordinator { get; } = TestCoordinatorSubstitutes.LiveDaq();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ILiveDaqKnownBoardsQuery KnownBoardsQuery { get; } = Substitute.For<ILiveDaqKnownBoardsQuery>();
    public LiveDaqStore LiveDaqStore { get; } = new(new InlineUiThreadDispatcher());
    public BehaviorSubject<LiveDaqManagementAvailability> ManagementAvailability { get; } = new(new(null, null));
    public Subject<LiveProtocolFrame> Frames { get; } = new();
    public BehaviorSubject<LiveDaqSharedStreamState> StreamStates { get; } = new(LiveDaqSharedStreamState.Empty);
    public BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> KnownBoardsChanges { get; } = new([]);
    public ILiveDaqSharedStreamLease StreamLease { get; } = Substitute.For<ILiveDaqSharedStreamLease>();

    public LiveDaqDetailViewModel CreateEditor(
        LivePreviewStartResult? startResult = null,
        LiveProtocolVersion protocolVersion = LiveProtocolVersion.V2)
    {
        currentCatalogSnapshot = DefaultSnapshot() with { ProtocolVersion = protocolVersion };
        ManagementAvailability.OnNext(new(new LiveDaqCatalogEntry(currentCatalogSnapshot.IdentityKey,
            currentCatalogSnapshot.DisplayName, currentCatalogSnapshot.BoardId, new ServiceEndpoint(currentCatalogSnapshot.Host!, currentCatalogSnapshot.Port!.Value), currentCatalogSnapshot.ProtocolVersion), null));
        var result = startResult ?? new LivePreviewStartResult.Started(
            LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 808));
        SharedStream.ConnectLiveAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                PublishStartResult(result);
                return Task.FromResult<LivePreviewStartResult?>(result);
            });

        return new LiveDaqDetailViewModel(
            currentCatalogSnapshot,
            SharedStream,
            LiveDaqCoordinator,
            Shell,
            DialogService,
            KnownBoardsQuery,
            LiveDaqStore,
            new InlineUiThreadDispatcher(),
            Time,
            UiTimers);
    }

    public void PublishStreamState(LiveDaqSharedStreamState state)
    {
        currentStreamState = state;
        ManagementAvailability.OnNext(state.IsClosed ? new(null, null) : ManagementAvailability.Value with
        {
            Holder = state.ConnectionState is LiveConnectionState.Disconnected ? null : new DaqTransportHolder.Streaming()
        });
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
        DampingSpeedCutoffs: DampingSpeedCutoffs.Default);

    private void PublishStartResult(LivePreviewStartResult result)
    {
        if (result is LivePreviewStartResult.Started started)
        {
            PublishStreamState(currentStreamState with
            {
                ConnectionState = new LiveConnectionState.Connected(),
                SessionHeader = started.Header,
                SelectedStreamMask = started.Header.AcceptedStreamMask,
            });
            return;
        }

        if (result is LivePreviewStartResult.NotStarted outcome)
        {
            PublishStreamState(currentStreamState with
            {
                ConnectionState = new LiveConnectionState.Disconnected(outcome),
                SessionHeader = null,
                SelectedStreamMask = LiveStreamMask.None,
            });
        }
    }
    public void Dispose()
    {
        LiveDaqStore.Dispose();
        ManagementAvailability.Dispose();
        Frames.Dispose();
        StreamStates.Dispose();
        KnownBoardsChanges.Dispose();
        UiTimers.Dispose();
    }
}
