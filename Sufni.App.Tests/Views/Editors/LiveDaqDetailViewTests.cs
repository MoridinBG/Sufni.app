using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Services.Management;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LiveDaqDetailViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqDetailView_RendersSectionsInSpecOrder()
    {
        var editor = CreateEditor();
        await using var mounted = await MountAsync(editor);

        var stack = mounted.View.GetVisualDescendants()
            .OfType<StackPanel>()
            .First(p => p.Children.OfType<Border>().Any(b => b.Name == "TravelCard"));

        var orderedNames = stack.Children
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();

        Assert.Equal(new[]
        {
            "TravelCard",
            "ImuCard",
            "GpsCard",
            "IdentityCard",
            "ConnectionCard",
            "RequestedRatesCard",
            "AcceptedSessionCard",
            "StartSessionButton",
            "DeviceManagementCard",
            "ManagementNotificationsBar",
            "ManagementErrorMessagesBar",
        }, orderedNames);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_RequestRateInputs_DisplayBoundValues_AndDisableWhenLocked()
    {
        var editor = CreateEditor();
        editor.RequestedTravelHz = 200;
        editor.RequestedImuHz = 100;
        editor.RequestedGpsFixHz = 5;

        await using var mounted = await MountAsync(editor);

        Assert.Equal(200, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedTravelHzUpDown")!.Value));
        Assert.Equal(100, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedImuHzUpDown")!.Value));
        Assert.Equal(5, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedGpsFixHzUpDown")!.Value));

        editor.AreRequestedRatesEnabled = false;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.View.FindControl<NumericUpDown>("RequestedTravelHzUpDown")!.IsEnabled);
        Assert.False(mounted.View.FindControl<NumericUpDown>("RequestedImuHzUpDown")!.IsEnabled);
        Assert.False(mounted.View.FindControl<NumericUpDown>("RequestedGpsFixHzUpDown")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_ConnectAndDisconnect_TrackVmGuards()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        editor.CanConnect = true;
        editor.CanDisconnect = false;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(mounted.View.FindControl<Button>("ConnectButton")!.IsEnabled);
        Assert.False(mounted.View.FindControl<Button>("DisconnectButton")!.IsEnabled);

        editor.CanConnect = false;
        editor.CanDisconnect = true;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(mounted.View.FindControl<Button>("ConnectButton")!.IsEnabled);
        Assert.True(mounted.View.FindControl<Button>("DisconnectButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_StartSessionButton_TracksCanStartSession()
    {
        var editor = CreateEditor();
        await using var mounted = await MountAsync(editor);

        Assert.False(mounted.View.FindControl<Button>("StartSessionButton")!.IsEnabled);

        editor.CanStartSession = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(mounted.View.FindControl<Button>("StartSessionButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_DeviceManagementButtons_DisabledWhenConnected()
    {
        var editor = CreateEditorWithManagement();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Connected, null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.View.FindControl<Button>("SetTimeButton")!.IsEnabled);
        Assert.False(mounted.View.FindControl<Button>("SelectConfigButton")!.IsEnabled);
        Assert.False(mounted.View.FindControl<Button>("UploadConfigButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_DeviceManagementButtons_EnabledWhenDisconnected()
    {
        var editor = CreateEditorWithManagement();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Disconnected, null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(mounted.View.FindControl<Button>("SetTimeButton")!.IsEnabled);
        Assert.True(mounted.View.FindControl<Button>("SelectConfigButton")!.IsEnabled);
        // Upload requires staged config, so remains disabled here.
        Assert.False(mounted.View.FindControl<Button>("UploadConfigButton")!.IsEnabled);

        editor.PendingConfigFileName = "CONFIG";
        editor.HasPendingConfig = true;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(mounted.View.FindControl<Button>("UploadConfigButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_AcceptedSessionTexts_DisplayBoundRates()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Connected, null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Travel: 200 Hz", mounted.View.FindControl<TextBlock>("AcceptedTravelRateTextBlock")!.Text);
        Assert.Equal("IMU: 100 Hz", mounted.View.FindControl<TextBlock>("AcceptedImuRateTextBlock")!.Text);
        Assert.Equal("GPS: 10 Hz", mounted.View.FindControl<TextBlock>("AcceptedGpsRateTextBlock")!.Text);
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailView_StatusTexts_DisplayConnectionAndError()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Disconnected, "Busy");
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Disconnected", mounted.View.FindControl<TextBlock>("ConnectionStateTextBlock")!.Text);
        var lastError = mounted.View.FindControl<TextBlock>("LastErrorTextBlock")!;
        Assert.Equal("Busy", lastError.Text);
        Assert.True(lastError.IsVisible);
    }

    private static LiveDaqDetailViewModel CreateEditor()
    {
        var editor = CreateEditorWithManagement();
        editor.Name = "Board 1";
        editor.CanConnect = true;
        editor.CanDisconnect = false;
        editor.CanStartSession = false;
        editor.AreRequestedRatesEnabled = true;
        editor.Snapshot = LiveDaqUiSnapshot.Empty;
        return editor;
    }

    private static LiveDaqDetailViewModel CreateEditorWithManagement()
    {
        var sharedStream = Substitute.For<ILiveDaqSharedStream>();
        sharedStream.Frames.Returns(new Subject<LiveProtocolFrame>());
        sharedStream.States.Returns(new BehaviorSubject<LiveDaqSharedStreamState>(LiveDaqSharedStreamState.Empty));
        sharedStream.CurrentState.Returns(LiveDaqSharedStreamState.Empty);
        sharedStream.RequestedConfiguration.Returns(LiveDaqStreamConfiguration.Default);

        var knownBoardsQuery = Substitute.For<ILiveDaqKnownBoardsQuery>();
        knownBoardsQuery.Changes.Returns(new BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>>([]));

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
            TestCoordinatorSubstitutes.LiveDaq(),
            Substitute.For<IDaqManagementService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>(),
            knownBoardsQuery,
            new LiveDaqStore())
        {
            Snapshot = LiveDaqUiSnapshot.Empty
        };
    }

    private static async Task<MountedLiveDaqDetailView> MountAsync(LiveDaqDetailViewModel editor)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var view = new LiveDaqDetailView
        {
            DataContext = editor
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedLiveDaqDetailView(host, view);
    }

    private static LiveDaqUiSnapshot CreateSnapshot(LiveConnectionState connectionState, string? error)
    {
        return new LiveDaqUiSnapshot(
            ConnectionState: connectionState,
            ConnectionStateText: LiveDaqUiSnapshot.ToConnectionStateText(connectionState),
            LastError: error,
            LastFrameReceivedUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Session: new LiveSessionContractSnapshot(
                SessionId: 42,
                SelectedSensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps,
                AcceptedTravelHz: 200,
                AcceptedImuHz: 100,
                AcceptedGpsFixHz: 10,
                SessionStartUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Flags: LiveSessionFlags.CalibratedOnly,
                ActiveImuLocations: [LiveImuLocation.Frame, LiveImuLocation.Rear]),
            Travel: new LiveTravelUiSnapshot(true, true, 112, 205, TimeSpan.FromSeconds(1.25), TimeSpan.FromMilliseconds(42), 3, 1),
            Imus:
            [
                new LiveImuUiSnapshot(LiveImuLocation.Frame, true, 10, 11, 12, 13, 14, 15, TimeSpan.FromSeconds(1.25), TimeSpan.FromMilliseconds(38), 4, 2),
                new LiveImuUiSnapshot(LiveImuLocation.Rear, true, 20, 21, 22, 23, 24, 25, TimeSpan.FromSeconds(1.25), TimeSpan.FromMilliseconds(38), 4, 2)
            ],
            Gps: new LiveGpsUiSnapshot(
                IsActive: true,
                HasData: true,
                PreviewState: new GpsPreviewState(true, true, GpsFixKind.ThreeDimensional, "3D fix"),
                FixTimestampUtc: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                Latitude: 48.2082,
                Longitude: 16.3738,
                Altitude: 180.5f,
                Speed: 7.5f,
                Heading: 125.5f,
                Satellites: 9,
                Epe2d: 1.2f,
                Epe3d: 2.3f,
                QueueDepth: 1,
                DroppedBatches: 0));
    }
}

internal sealed class MountedLiveDaqDetailView(Window host, LiveDaqDetailView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqDetailView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
