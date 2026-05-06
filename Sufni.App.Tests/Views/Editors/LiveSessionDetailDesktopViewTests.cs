using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.DesktopViews.Items;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Editors;

public class LiveSessionDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionDetailDesktopView_RendersLiveShellContent()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var textBlocks = mounted.View.GetVisualDescendants().OfType<TextBlock>().ToArray();
        var controls = mounted.View.GetVisualDescendants().OfType<Control>().ToArray();
        var shellView = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();

        Assert.Equal("State: Connected", textBlocks.First(textBlock => textBlock.Name == "LiveConnectionStateTextBlock").Text);
        Assert.Equal("Session: 909", textBlocks.First(textBlock => textBlock.Name == "LiveSessionIdTextBlock").Text);
        Assert.NotNull(controls.FirstOrDefault(control => control.Name == "TabControl"));
        Assert.IsType<LiveSessionGraphDesktopView>(shellView.GraphContent);
        Assert.IsType<SessionMediaDesktopView>(shellView.MediaContent);
        Assert.IsType<SessionStatisticsDesktopView>(shellView.StatisticsContent);
        Assert.IsType<LiveSessionControlsDesktopView>(shellView.ControlContent);
        Assert.IsType<SessionSidebarDesktopView>(shellView.SidebarContent);
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
        var graphBatches = new ReplaySubject<LiveGraphBatch>(1);
        var header = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 909);
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
                CaptureDuration: TimeSpan.FromSeconds(3),
                TravelQueueDepth: 0,
                ImuQueueDepth: 0,
                GpsQueueDepth: 0,
                TravelDroppedBatches: 0,
                ImuDroppedBatches: 0,
                GpsDroppedBatches: 0,
                CanSave: true),
            CaptureRevision: 1);

        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        liveSessionService.Current.Returns(snapshot);
        liveSessionService.Snapshots.Returns(new BehaviorSubject<LiveSessionPresentationSnapshot>(snapshot));
        liveSessionService.GraphBatches.Returns(graphBatches);
        liveSessionService.EnsureAttachedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        liveSessionService.DisposeAsync().Returns(ValueTask.CompletedTask);

        graphBatches.OnNext(new LiveGraphBatch(
            Revision: 1,
            TravelTimes: [0.0, 0.01],
            FrontTravel: [10.0, 11.0],
            RearTravel: [9.0, 10.0],
            VelocityTimes: [0.0, 0.01],
            FrontVelocity: [100.0, 110.0],
            RearVelocity: [90.0, 100.0],
            ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [0.0, 0.01],
            },
            ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [1.0, 1.5],
            }));

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
            Name = "Live Session 01"
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

    private static async Task<MountedLiveSessionDetailDesktopView> MountAsync(LiveSessionDetailViewModel editor)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new LiveSessionDetailDesktopView
        {
            DataContext = editor
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedLiveSessionDetailDesktopView(host, view);
    }
}

internal sealed class MountedLiveSessionDetailDesktopView(Window host, LiveSessionDetailDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveSessionDetailDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
