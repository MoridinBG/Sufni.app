using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Editors;

internal sealed class SessionDetailViewTestContext
{
    private const string DefaultSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"12\"><rect width=\"16\" height=\"12\" fill=\"#8899AA\" /></svg>";

    private readonly SessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionGraph recordedSessionGraph = Substitute.For<IRecordedSessionGraph>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ISessionAnalysisService sessionAnalysisService = Substitute.For<ISessionAnalysisService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public SessionDetailViewTestContext()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(SessionPreferences.Default));
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        sessionPresentationService.CalculateDamperPercentages(Arg.Any<TelemetryData>(), Arg.Any<TelemetryTimeRange?>())
            .Returns(new SessionDamperPercentages(null, null, null, null, null, null, null, null));
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
        var telemetry = TestTelemetryData.Create();
        if (includeImu)
        {
            telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        }

        return new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            FullTrackId: null,
            FullTrackPoints: null,
            TrackPoints: null,
            MapVideoWidth: null,
            DamperPercentages: new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80)));
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
            BalanceAvailable: includeBalance),
            includeTelemetry ? TestTelemetryData.Create() : null,
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

        var editor = CreateEditor(snapshot);
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

    private SessionDetailViewModel CreateEditor(SessionSnapshot snapshot)
    {
        return new SessionDetailViewModel(
            snapshot,
            sessionCoordinator,
            sessionStore,
            recordedSessionGraph,
            sessionPresentationService,
            sessionAnalysisService,
            tileLayerService,
            shell,
            dialogService,
            sessionPreferences);
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
