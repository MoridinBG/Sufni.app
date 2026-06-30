using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Tests.TestSupport;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.LiveDaq.DesktopViews.Editors;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.DesktopViews.Items;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Views.Shared;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Detail.DesktopViews.Editors;
using Sufni.App.Sessions.Detail.DesktopViews.Items;
using Sufni.App.Sessions.Media.DesktopViews.Items;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Statistics.DesktopViews.Items;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Tests.Views.Editors;

[Collection("Ui")]
public class LiveSessionDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionDetailDesktopView_RendersLiveShellContent()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var shellView = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();

        Assert.IsType<LiveSessionGraphDesktopView>(shellView.GraphContent);
        Assert.IsType<SessionMediaDesktopView>(shellView.MediaContent);
        Assert.IsType<SessionStatisticsDesktopView>(shellView.StatisticsContent);
        Assert.IsType<LiveSessionControlsDesktopView>(shellView.ControlContent);
        Assert.IsType<SessionSidebarDesktopView>(shellView.SidebarContent);
    }

    [AvaloniaFact]
    public async Task LiveSessionDetailDesktopView_HeaderFields_UseDefaultFontSize()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var headerFields = mounted.View.GetVisualDescendants().OfType<LiveSessionHeaderFields>().Single();

        Assert.Equal(12, headerFields.FieldFontSize);
    }

    private static LiveSessionDetailViewModel CreateEditor()
    {
        var sessionCoordinator = TestCoordinatorSubstitutes.Session();
        var sessionPresentationService = Substitute.For<ISessionPresentationService>();
        var backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var graphBatches = new ReplaySubject<LiveGraphBatch>(1);
        var header = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 909);
        var snapshot = new LiveSessionPresentationSnapshot(
            Stream: new LiveSessionStreamPresentation.Streaming(header.SessionStartUtc.LocalDateTime, header),
            StatisticsTelemetry: null,
            DamperPercentages: SessionDamperPercentages.Empty,
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

        var liveSessionService = StubLiveSessionService.WithDefaultLiveStream(snapshot, graphBatches);

        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);

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
            ImuVibrationRms: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [1.0, 1.5],
            },
            FramePitchRollTimes: [],
            FramePitchDegrees: [],
            FrameRollDegrees: []));

        return new LiveSessionDetailViewModel(
            CreateSessionContext(),
            liveSessionService,
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            new InlineUiThreadDispatcher())
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
            BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(
                new LiveDaqTravelChannelCalibration(180, measurement => measurement),
                new LiveDaqTravelChannelCalibration(170, measurement => measurement)),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(Guid.Empty, 0));
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
