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
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Models;

using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.Views.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Detail.Views.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.LiveDaq.Views.Editors;

[Collection("Ui")]
public class LiveSessionDetailViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionDetailView_HostsSessionShell_WithLivePageSet_AndControlsStrip()
    {
        var editor = CreateEditor();

        await using var mounted = await MountAsync(editor);

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellMobileView>().SingleOrDefault();
        Assert.NotNull(shell);

        var carousel = mounted.View.GetVisualDescendants()
            .OfType<CarouselPage>()
            .FirstOrDefault(c => c.Name == "SessionCarouselPage");
        var pager = mounted.View.GetVisualDescendants()
            .OfType<PipsPager>()
            .FirstOrDefault(c => c.Name == "SessionPipsPager");
        Assert.NotNull(carousel);
        Assert.Same(editor.Pages, carousel!.ItemsSource);
        Assert.NotNull(pager);
        Assert.True(pager!.NumberOfPages > 0);

        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<EditableTitle>().FirstOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<ErrorMessagesBar>().FirstOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<CommonButtonLine>().FirstOrDefault());
        Assert.IsType<LiveSessionControlsMobileView>(shell!.ControlContent);
    }

    private static LiveSessionDetailViewModel CreateEditor()
    {
        var liveDaqCoordinator = TestCoordinatorSubstitutes.LiveDaq();
        var sessionPresentationService = Substitute.For<ISessionPresentationService>();
        var backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        var signalBatches = new Subject<LiveSignalBatch>();
        var header = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 401);
        var snapshot = new LiveSessionPresentationSnapshot(
            AnalysisTelemetry: null,
            DampingPercentages: SessionDampingPercentages.Empty,
            SessionTrackPoints: [],
            Controls: new LiveSessionControlState(
                ConnectionState: new LiveConnectionState.Connected(),
                LastError: null,
                SessionHeader: header,
                CaptureStartUtc: header.SessionStartUtc,
                CaptureDuration: TimeSpan.FromSeconds(2),
                CanSave: false),
            CaptureRevision: 1);

        var liveSessionService = StubLiveSessionService.WithDefaultLiveStream(snapshot, signalBatches);

        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);

        return new LiveSessionDetailViewModel(
            CreateSessionContext(),
            liveSessionService,
            liveDaqCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            new InlineUiThreadDispatcher(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            new Sufni.App.Tests.TestSupport.Async.ManualUiTimerScheduler())
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
            BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(
                new LiveDaqTravelChannelCalibration(180, measurement => measurement),
                new LiveDaqTravelChannelCalibration(170, measurement => measurement)),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(Guid.Empty, 0));
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
