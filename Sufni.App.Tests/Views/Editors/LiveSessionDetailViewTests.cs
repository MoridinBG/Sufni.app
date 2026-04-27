using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Editors;

public class LiveSessionDetailViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionDetailView_HostsSessionShell_WithLivePageSet_AndControlsStrip()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellMobileView>().SingleOrDefault();
        Assert.NotNull(shell);

        var tabHeaders = mounted.View.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(c => c.Name == "TabHeaders");
        Assert.NotNull(tabHeaders);
        Assert.Equal(editor.Pages.Count, tabHeaders!.ItemCount);
        Assert.Equal(["Graph", "Spring", "Damper", "Notes"], editor.Pages.Select(page => page.DisplayName));

        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<EditableTitle>().FirstOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<ErrorMessagesBar>().FirstOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<CommonButtonLine>().FirstOrDefault());
        Assert.IsType<LiveSessionControlsMobileView>(shell!.ControlContent);
    }

    [AvaloniaFact]
    public async Task LiveSessionDetailView_SaveAndResetButtons_BindToEditorCommands()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var buttons = mounted.View.GetVisualDescendants().OfType<Button>().ToArray();
        var saveButton = buttons.First(b => b.Name == "SaveButton");
        var resetButton = buttons.First(b => b.Name == "ResetButton");

        Assert.Same(editor.SaveCommand, saveButton.Command);
        Assert.Same(editor.ResetCommand, resetButton.Command);
    }

    [AvaloniaFact]
    public async Task LiveSessionDetailView_ShowsScreenError_WhenScreenStateIsError()
    {
        var editor = CreateEditor();
        editor.ScreenState = SessionScreenPresentationState.Error("stream dropped");

        await using var mounted = await MountAsync(editor);

        var errorHeading = mounted.View.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Could not load session");
        var errorBody = mounted.View.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "stream dropped");

        Assert.NotNull(errorHeading);
        Assert.NotNull(errorBody);
    }

    private static LiveSessionDetailViewModel CreateEditor()
    {
        var liveSessionService = Substitute.For<ILiveSessionService>();
        var sessionCoordinator = TestCoordinatorSubstitutes.Session();
        var sessionPresentationService = Substitute.For<ISessionPresentationService>();
        var backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
        var tileLayerService = Substitute.For<ITileLayerService>();
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var graphBatches = new Subject<LiveGraphBatch>();
        var header = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 401);
        var snapshot = new LiveSessionPresentationSnapshot(
            Stream: new LiveSessionStreamPresentation.Streaming(header.SessionStartUtc.LocalDateTime, header),
            StatisticsTelemetry: null,
            DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
            SessionTrackPoints: [],
            Controls: new LiveSessionControlState(
                ConnectionState: LiveConnectionState.Connected,
                LastError: null,
                SessionHeader: header,
                CaptureStartUtc: header.SessionStartUtc,
                CaptureDuration: TimeSpan.FromSeconds(2),
                TravelQueueDepth: 0,
                ImuQueueDepth: 0,
                GpsQueueDepth: 0,
                TravelDroppedBatches: 0,
                ImuDroppedBatches: 0,
                GpsDroppedBatches: 0,
                CanSave: false),
            CaptureRevision: 1);

        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        liveSessionService.Current.Returns(snapshot);
        liveSessionService.Snapshots.Returns(new BehaviorSubject<LiveSessionPresentationSnapshot>(snapshot));
        liveSessionService.GraphBatches.Returns(graphBatches);
        liveSessionService.EnsureAttachedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        liveSessionService.DisposeAsync().Returns(ValueTask.CompletedTask);

        return new LiveSessionDetailViewModel(
            CreateSessionContext(),
            liveSessionService,
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            tileLayerService,
            shell,
            dialogService)
        {
            Name = "Live Session 01",
        };
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
            TravelCalibration: new LiveDaqTravelCalibration(
                new LiveDaqTravelChannelCalibration(180, measurement => measurement),
                new LiveDaqTravelChannelCalibration(170, measurement => measurement)));
    }

    private static async Task<MountedLiveSessionDetailView> MountAsync(LiveSessionDetailViewModel editor)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: false);
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new LiveSessionDetailView
        {
            DataContext = editor,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedLiveSessionDetailView(host, view);
    }
}

internal sealed record MountedLiveSessionDetailView(Window Host, LiveSessionDetailView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
