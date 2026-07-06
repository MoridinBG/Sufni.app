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
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.DesktopViews.Editors;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Detail.Views.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
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
    private readonly IRecordedSessionProjection recordedSessionProjection = Substitute.For<IRecordedSessionProjection>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ISessionInsightsService sessionAnalysisService = Substitute.For<ISessionInsightsService>();
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
        sessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(SessionInsightsResult.Hidden);
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

    public SessionDetailLoadResult.Loaded CreateLoadedState(
        bool includeImu = false,
        bool includeBalance = true)
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        if (includeImu)
        {
            telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        }

        var percentages = new SessionDampingPercentages(10, 20, 30, 40, 50, 60, 70, 80);
        var cachePresentation = new SessionCachePresentationData(
            FrontTravelDistribution: DefaultSvg,
            RearTravelDistribution: DefaultSvg,
            FrontVelocityDistribution: DefaultSvg,
            RearVelocityDistribution: DefaultSvg,
            CompressionBalance: includeBalance ? DefaultSvg : null,
            ReboundBalance: includeBalance ? DefaultSvg : null,
            DampingPercentages: percentages,
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            BalanceAvailable: includeBalance);

        return new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                telemetry,
                FullTrackId: null,
                FullTrackPoints: null,
                TrackPoints: null,
                MediaColumnWidth: null,
                DampingPercentages: percentages,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                DampingSpeedCutoffOwner: null),
            cachePresentation));
    }

    public async Task<MountedSessionDetailView<SessionDetailView>> MountMobileAsync(
        SessionSnapshot? snapshot = null,
        SessionDetailLoadResult? loadResult = null,
        Task<SessionDetailLoadResult>? loadTask = null)
    {
        snapshot ??= CreateTelemetryLightSnapshot();
        ConfigureStores(snapshot);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<IProgress<SessionDetailLoadProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => loadTask ?? Task.FromResult(loadResult ?? CreateLoadedState()));

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
        SessionDetailLoadResult? loadResult = null,
        Task<SessionDetailLoadResult>? loadTask = null)
    {
        snapshot ??= CreateTelemetryBearingSnapshot();
        ConfigureStores(snapshot);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<IProgress<SessionDetailLoadProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => loadTask ?? Task.FromResult(loadResult ?? CreateLoadedState()));

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
        recordedSessionProjection.WatchSession(snapshot.Id).Returns(Observable.Empty<RecordedSessionDomainSnapshot>());
        sessionStore.Get(snapshot.Id).Returns(snapshot);
    }

    private SessionDetailViewModel CreateEditor(SessionSnapshot snapshot, bool isDesktopLayout = true)
    {
        var dispatcher = new InlineUiThreadDispatcher();
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(sessionAnalysisService),
            new InlineBackgroundTaskRunner(),
            dispatcher);
        return new SessionDetailViewModel(
            snapshot,
            sessionCoordinator,
            trackCoordinator,
            sessionStore,
            recordedSessionProjection,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            sessionPreferences,
            dispatcher,
            deferDomainHandlingWhenInactive: isDesktopLayout,
            new InMemoryRecordedSessionProcessingOptionCache(),
            new TestSessionProcessedTelemetryReader(),
            analysisResultStateFactory,
            Substitute.For<IRecordedSessionDerivationWindowCache>(),
            () => Substitute.For<IEditorFactory>());
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
