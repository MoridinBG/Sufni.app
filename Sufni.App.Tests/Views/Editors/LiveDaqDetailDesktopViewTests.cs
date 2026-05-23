using System;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LiveDaqDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_RequestRateInputs_DisplayBoundValues()
    {
        var editor = CreateEditor();
        editor.RequestedTravelHz = 200;
        editor.RequestedImuHz = 100;
        editor.RequestedGpsFixHz = 5;

        await using var mounted = await MountAsync(editor);

        Assert.Equal(200, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedTravelHzUpDown")!.Value));
        Assert.Equal(100, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedImuHzUpDown")!.Value));
        Assert.Equal(5, Convert.ToInt32(mounted.View.FindControl<NumericUpDown>("RequestedGpsFixHzUpDown")!.Value));
    }

    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_PendingConfigText_ReflectsStagedFile()
    {
        var editor = CreateEditorWithManagement();

        await using var mounted = await MountAsync(editor);
        editor.Snapshot = CreateSnapshot(LiveConnectionState.Disconnected, null);
        editor.PendingConfigFileName = "CONFIG";
        editor.HasPendingConfig = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        var text = mounted.View.FindControl<TextBlock>("PendingConfigTextBlock");
        Assert.NotNull(text);
        Assert.True(text!.IsVisible);
        Assert.Equal("Staged: CONFIG", text.Text);
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
            new LiveDaqStore(),
            new InlineUiThreadDispatcher())
        {
            Snapshot = LiveDaqUiSnapshot.Empty
        };
    }

    private static async Task<MountedLiveDaqDetailDesktopView> MountAsync(LiveDaqDetailViewModel editor)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new LiveDaqDetailDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedLiveDaqDetailDesktopView(host, view);
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
                RequestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.RearImu | LiveSensorInstanceMask.Gps,
                AcceptedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.RearImu | LiveSensorInstanceMask.Gps,
                AcceptedTravelHz: 200,
                AcceptedImuHz: 100,
                AcceptedGpsFixHz: 10,
                SessionStartUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Flags: LiveSessionFlags.CalibratedOnly,
                ActiveImuLocations: [LiveImuLocation.Frame, LiveImuLocation.Rear]),
            Travel: new LiveTravelUiSnapshot(true, true, true, true, 112, 205, TimeSpan.FromSeconds(1.25), TimeSpan.FromMilliseconds(42), 3, 1),
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

internal sealed class MountedLiveDaqDetailDesktopView(Window host, LiveDaqDetailDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqDetailDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
