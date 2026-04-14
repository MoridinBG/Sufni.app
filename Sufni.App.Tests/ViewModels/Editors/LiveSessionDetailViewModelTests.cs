using System;
using System.Reactive.Subjects;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LiveSessionDetailViewModelTests
{
    private readonly ILiveDaqSharedStream sharedStream = Substitute.For<ILiveDaqSharedStream>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly ILiveDaqSharedStreamLease observerLease = Substitute.For<ILiveDaqSharedStreamLease>();
    private readonly ILiveDaqSharedStreamLease configurationLockLease = Substitute.For<ILiveDaqSharedStreamLease>();
    private readonly BehaviorSubject<LiveDaqSharedStreamState> streamStates = new(LiveDaqSharedStreamState.Empty);

    private readonly LiveSessionHeader sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 77);
    private LiveDaqSharedStreamState currentState = LiveDaqSharedStreamState.Empty;

    public LiveSessionDetailViewModelTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);

        sharedStream.States.Returns(streamStates);
        sharedStream.CurrentState.Returns(_ => currentState);
        sharedStream.AcquireLease().Returns(observerLease);
        sharedStream.AcquireConfigurationLock().Returns(configurationLockLease);
        observerLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        configurationLockLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        sharedStream.EnsureStartedAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                currentState = currentState with
                {
                    ConnectionState = LiveConnectionState.Connected,
                    LastError = null,
                    SessionHeader = sessionHeader,
                    SelectedSensorMask = LiveSensorMask.Travel | LiveSensorMask.Imu,
                };
                streamStates.OnNext(currentState);
                return Task.FromResult<LivePreviewStartResult?>(
                    new LivePreviewStartResult.Started(sessionHeader, LiveSensorMask.Travel | LiveSensorMask.Imu));
            });
    }

    [AvaloniaFact]
    public async Task Loaded_AcquiresLeases_InitializesMap_AndStartsStream()
    {
        var editor = CreateEditor();

        await editor.LoadedCommand.ExecuteAsync(null);

        sharedStream.Received(1).AcquireLease();
        sharedStream.Received(1).AcquireConfigurationLock();
        await tileLayerService.Received(1).InitializeAsync();
        await sharedStream.Received(1).EnsureStartedAsync(Arg.Any<CancellationToken>());
        Assert.Equal(LiveConnectionState.Connected, editor.ControlState.ConnectionState);
        Assert.Equal(sessionHeader.SessionStartUtc.LocalDateTime, editor.Timestamp);
    }

    [AvaloniaFact]
    public async Task Unloaded_ReleasesConfigurationLock_AndObserverLease()
    {
        var editor = CreateEditor();
        await editor.LoadedCommand.ExecuteAsync(null);

        await editor.UnloadedCommand.ExecuteAsync(null);

        await configurationLockLease.Received(1).DisposeAsync();
        await observerLease.Received(1).DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ResetCommand_RevertsSidebarEdits_AndSaveRemainsDisabled()
    {
        var editor = CreateEditor();
        var originalName = editor.Name;

        editor.Name = "Custom live session";
        editor.DescriptionText = "first lap";
        editor.ForkSettings.SpringRate = "550 lb/in";

        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.ResetCommand.CanExecute(null));

        await editor.ResetCommand.ExecuteAsync(null);

        Assert.Equal(originalName, editor.Name);
        Assert.Null(editor.DescriptionText);
        Assert.Null(editor.ForkSettings.SpringRate);
        Assert.False(editor.ResetCommand.CanExecute(null));
    }

    private LiveSessionDetailViewModel CreateEditor()
    {
        return new LiveSessionDetailViewModel(CreateSessionContext(), sharedStream, tileLayerService, shell, dialogService);
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
}