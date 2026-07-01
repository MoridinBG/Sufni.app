using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NSubstitute;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.DesktopViews.Editors;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Detail.Views.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.Sessions.Detail.Views.Editors;

internal sealed class SessionDetailViewTestContext
{
    private const string DefaultSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"12\"><rect width=\"16\" height=\"12\" fill=\"#8899AA\" /></svg>";

    private readonly ISessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionGraph recordedSessionGraph = Substitute.For<IRecordedSessionGraph>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ISessionAnalysisService sessionAnalysisService = Substitute.For<ISessionAnalysisService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public SessionDetailViewTestContext()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(SessionPreferences.Default));
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        sessionPresentationService.CalculateDamperPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDamperPercentages.Empty);
        sessionAnalysisService.Analyze(Arg.Any<SessionAnalysisRequest>()).Returns(SessionAnalysisResult.Hidden);
    }

    public SessionSnapshot CreateTelemetryBearingSnapshot(
        string name = "Recorded Session 01",
        string description = "Suspension notes",
        bool hasProcessedData = true)
    {
        return TestSnapshots.Session(
            name: name,
            description: description,
            hasProcessedData: hasProcessedData);
    }

    public SessionSnapshot CreateTelemetryLightSnapshot(
        string name = "Recorded Session 01",
        string description = "Suspension notes",
        bool hasProcessedData = true)
    {
        return TestSnapshots.Session(
            name: name,
            description: description,
            hasProcessedData: hasProcessedData);
    }

    public SessionDesktopLoadResult.Loaded CreateDesktopLoadedState(bool includeImu = false)
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        if (includeImu)
        {
            telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        }

        return new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            FullTrackId: null,
            FullTrackPoints: null,
            TrackPoints: null,
            MediaColumnWidth: null,
            DamperPercentages: new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: null));
    }

    public SessionMobileLoadResult.LoadedFromCache CreateMobileLoadedState(
        bool includeBalance = true,
        bool includeTelemetry = true)
    {
        return new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            FrontTravelHistogram: DefaultSvg,
            RearTravelHistogram: DefaultSvg,
            FrontVelocityHistogram: DefaultSvg,
            RearVelocityHistogram: DefaultSvg,
            CompressionBalance: includeBalance ? DefaultSvg : null,
            ReboundBalance: includeBalance ? DefaultSvg : null,
            DamperPercentages: new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            BalanceAvailable: includeBalance),
            includeTelemetry ? TestTelemetryData.CreateProcessed() : null,
            null);
    }

    public async Task<MountedSessionDetailView<SessionDetailView>> MountMobileAsync(
        SessionSnapshot? snapshot = null,
        SessionMobileLoadResult? loadResult = null)
    {
        snapshot ??= CreateTelemetryLightSnapshot();
        ConfigureStores(snapshot);
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(loadResult ?? CreateMobileLoadedState());

        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: false);

        var editor = CreateEditor(snapshot, isDesktopLayout: false);
        var view = new SessionDetailView
        {
            DataContext = editor
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionDetailView<SessionDetailView>(host, view, editor);
    }

    public async Task<MountedSessionDetailView<SessionDetailDesktopView>> MountDesktopAsync(
        SessionSnapshot? snapshot = null,
        SessionDesktopLoadResult? loadResult = null)
    {
        snapshot ??= CreateTelemetryBearingSnapshot();
        ConfigureStores(snapshot);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(loadResult ?? CreateDesktopLoadedState());

        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var editor = CreateEditor(snapshot);
        var view = new SessionDetailDesktopView
        {
            DataContext = editor
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionDetailView<SessionDetailDesktopView>(host, view, editor);
    }

    private void ConfigureStores(SessionSnapshot snapshot)
    {
        recordedSessionGraph.WatchSession(snapshot.Id).Returns(Observable.Empty<RecordedSessionDomainSnapshot>());
        sessionStore.Get(snapshot.Id).Returns(snapshot);
    }

    private SessionDetailViewModel CreateEditor(SessionSnapshot snapshot, bool isDesktopLayout = true)
    {
        return new SessionDetailViewModel(
            snapshot,
            sessionCoordinator,
            trackCoordinator,
            sessionStore,
            recordedSessionGraph,
            sessionPresentationService,
            sessionAnalysisService,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            sessionPreferences,
            new InlineUiThreadDispatcher(),
            isDesktopLayout
                ? new DesktopSessionLayoutStrategy()
                : new MobileSessionLayoutStrategy(),
            new InMemoryRecordedSessionProcessingOptionCache());
    }
}

internal sealed class MountedSessionDetailView<TView>(Window host, TView view, SessionDetailViewModel editor) : IAsyncDisposable
    where TView : Control
{
    public Window Host { get; } = host;
    public TView View { get; } = view;
    public SessionDetailViewModel Editor { get; } = editor;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
