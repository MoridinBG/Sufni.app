using System.Reactive.Linq;
using Avalonia;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Video.ViewModels;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Sessions;

internal sealed class SessionDetailHarness
{
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();

    public SessionDetailHarness()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        SessionPreferences.WithDefaultObserveRecorded();
        SessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(global::Sufni.App.Infrastructure.SessionPreferences.Default));
        SessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<global::Sufni.App.Infrastructure.SessionPreferences, global::Sufni.App.Infrastructure.SessionPreferences>>())
            .Returns(Task.CompletedTask);
        SessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(SessionInsightsResult.Hidden);
    }

    public ISessionCoordinator SessionCoordinator { get; } = TestCoordinatorSubstitutes.Session();
    public ISessionStore SessionStore { get; } = Substitute.For<ISessionStore>();
    public IRecordedSessionProjection RecordedSessionProjection { get; } = Substitute.For<IRecordedSessionProjection>();
    public ISessionPresentationService SessionPresentationService { get; } = Substitute.For<ISessionPresentationService>();
    public ISessionInsightsService SessionInsightsService { get; } = Substitute.For<ISessionInsightsService>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ISessionPreferences SessionPreferences { get; } = Substitute.For<ISessionPreferences>();
    public TestSessionProcessedTelemetryReader ProcessedTelemetryReader { get; } = new();
    public ISessionEditingCoordinator SessionEditingCoordinator { get; } = Substitute.For<ISessionEditingCoordinator>();
    public IVideoPaneFactory? VideoPaneFactory { get; set; }
    public SessionSegmentStore SegmentStore { get; } = new(Substitute.For<ISessionSegmentRepository>(), new InlineUiThreadDispatcher());
    public ISegmentCoordinator SegmentCoordinator { get; } = Substitute.For<ISegmentCoordinator>();
    public SessionOperationRunner OperationRunner { get; } = new();
    public IUiThreadDispatcher UiThreadDispatcher { get; set; } = new InlineUiThreadDispatcher();
    public IObservable<RecordedSessionDomainSnapshot> ProjectionChanges { get; private set; } = Observable.Empty<RecordedSessionDomainSnapshot>();

    public SessionSnapshot CreateRecordedSession(
        string name = "Recorded Session 01",
        string description = "Suspension notes",
        bool hasProcessedData = true,
        long updated = 1) =>
        TestSnapshots.Session(name: name, description: description, hasProcessedData: hasProcessedData, updated: updated);

    public SessionDetailViewModel CreateLoadedRecordedSession(
        SessionSnapshot? snapshot = null,
        SessionDetailLoadResult? loadResult = null,
        IObservable<RecordedSessionDomainSnapshot>? projectionChanges = null,
        bool isDesktop = true)
    {
        snapshot ??= CreateRecordedSession();
        ConfigureSnapshot(snapshot, projectionChanges);
        ConfigureLoad(snapshot, loadResult ?? CreateLoadedResult());
        return CreateEditor(snapshot, isDesktop);
    }

    public Task LoadAsync(SessionDetailViewModel editor) =>
        editor.LoadedCommand.ExecuteAsync(null);

    public Task SaveAsync(SessionDetailViewModel editor) =>
        editor.SaveCommand.ExecuteAsync(null);

    public Task DeleteAsync(SessionDetailViewModel editor, bool navigateBack = true) =>
        editor.DeleteCommand.ExecuteAsync(navigateBack);

    public void Unload(SessionDetailViewModel editor) =>
        editor.UnloadedCommand.Execute(null);

    public void ConfigureSnapshot(SessionSnapshot snapshot, IObservable<RecordedSessionDomainSnapshot>? projectionChanges = null)
    {
        ProjectionChanges = projectionChanges ?? Observable.Empty<RecordedSessionDomainSnapshot>();
        RecordedSessionProjection.WatchSession(snapshot.Id).Returns(ProjectionChanges);
        SessionStore.Get(snapshot.Id).Returns(snapshot);
    }

    public void ConfigureLoad(SessionSnapshot snapshot, SessionDetailLoadResult result)
    {
        var firstContentResult = result;
        if (result is SessionDetailLoadResult.Loaded loaded)
        {
            var presentation = loaded.Data.TelemetryPresentation;
            firstContentResult = new SessionDetailLoadResult.Loaded(
                new SessionDetailData(
                    presentation with
                    {
                        FullTrackPoints = null,
                        TrackPoints = null,
                        MediaColumnWidth = null,
                    }));
            SessionCoordinator.LoadTrackAsync(
                    snapshot.Id,
                    presentation.TelemetryData,
                    Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                    Arg.Any<CancellationToken>())
                .Returns(new SessionDetailTrackLoadResult.Loaded(
                    new SessionTrackPresentationData(
                        presentation.FullTrackId,
                        presentation.FullTrackPoints,
                        presentation.TrackPoints,
                        presentation.MediaColumnWidth)));
        }

        SessionCoordinator.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(firstContentResult);
    }

    public SessionDetailLoadResult.Loaded CreateLoadedResult(
        TelemetryData? telemetry = null,
        bool includeBalance = true,
        IReadOnlyList<TrackPoint>? trackPoints = null,
        double? mediaColumnWidth = null)
    {
        var data = telemetry ?? TestTelemetryData.CreateProcessed(rearPresent: includeBalance);
        return new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                data,
                FullTrackId: null,
                FullTrackPoints: null,
                TrackPoints: trackPoints?.ToList(),
                MediaColumnWidth: mediaColumnWidth,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                DampingSpeedCutoffOwner: null)));
    }

    private SessionDetailViewModel CreateEditor(
        SessionSnapshot snapshot,
        bool isDesktop)
    {
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(SessionInsightsService),
            new InlineBackgroundTaskRunner(),
            UiThreadDispatcher);
        return new SessionDetailViewModel(
            snapshot,
            SessionCoordinator,
            trackCoordinator,
            SessionStore,
            RecordedSessionProjection,
            new TestMapViewModelFactory(tileLayerService),
            Shell,
            DialogService,
            SessionPreferences,
            UiThreadDispatcher,
            deferDomainHandlingWhenInactive: isDesktop,
            ProcessedTelemetryReader,
            analysisResultStateFactory,
            SessionEditingCoordinator,
            TestSegmentPickers.CreateFactory(SegmentStore, SegmentCoordinator, DialogService),
            TestSegmentComparisons.CreateFactory(SegmentCoordinator),
            OperationRunner,
            videoPaneFactory: VideoPaneFactory);
    }
}
