using DynamicData;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Sessions.Services.Preferences;
using Sufni.App.Shared.Views.Dialogs;
using Sufni.App.Shared.Stores;
using Microsoft.Extensions.Time.Testing;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
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
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.TestSupport.Sessions;

internal sealed class SessionDetailHarness : IDisposable
{
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();

    private readonly BehaviorSubject<SessionPreferences> recordedPreferences = new(global::Sufni.App.Sessions.Models.SessionPreferences.Default);

    public void PublishPreferences(SessionPreferences preferences) => recordedPreferences.OnNext(preferences);

    public SessionDetailHarness()
    {
        RecordedSessionProjection.ConnectSessions().Returns(SessionSummaries.Connect());
        SegmentStore = new SessionSegmentStore(publication);
        SessionPreferences.ObserveRecorded(Arg.Any<Guid>()).Returns(recordedPreferences);
        SessionCoordinator.LoadPreferencesAsync(Arg.Any<Guid>())
            .Returns(_ => new SessionPreferenceLoadResult.Loaded(recordedPreferences.Value));
        SessionCoordinator.SavePreferencesAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(call =>
            {
                PublishPreferences(call.Arg<Func<SessionPreferences, SessionPreferences>>()(recordedPreferences.Value));
                return new SessionPreferenceSaveResult.Saved();
            });
        SessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(global::Sufni.App.Sessions.Models.SessionPreferences.Default));
        SessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<global::Sufni.App.Sessions.Models.SessionPreferences, global::Sufni.App.Sessions.Models.SessionPreferences>>())
            .Returns(Task.CompletedTask);
        SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(SessionInsightsResult.Hidden);
    }

    public InMemoryTerrainProfileRepository TerrainProfiles { get; } = new();
    public ITerrainProfileCoordinator TerrainCoordinator { get; } = Substitute.For<ITerrainProfileCoordinator>();
    public IBackgroundTaskRunner BackgroundRunner { get; set; } = new InlineBackgroundTaskRunner();
    public IBikeCoordinator BikeCoordinator { get; } = TestCoordinatorSubstitutes.Bike();
    public ISessionCoordinator SessionCoordinator { get; } = TestCoordinatorSubstitutes.Session();
    public ISessionLoader SessionLoader { get; } = TestCoordinatorSubstitutes.SessionLoader();
    public ISessionStore SessionStore { get; } = Substitute.For<ISessionStore>();
    public SourceCache<RecordedSessionSummary, Guid> SessionSummaries { get; } = new(s => s.Id);
    public IRecordedSessionProjection RecordedSessionProjection { get; } = Substitute.For<IRecordedSessionProjection>();
    public ISessionInsightsService SessionInsightsService { get; } = Substitute.For<ISessionInsightsService>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
    public ISessionPreferences SessionPreferences { get; } = Substitute.For<ISessionPreferences>();
    public TestSessionProcessedTelemetryReader ProcessedTelemetryReader { get; } = new();
    public ISessionEditingCoordinator SessionEditingCoordinator { get; } = Substitute.For<ISessionEditingCoordinator>();
    public IVideoPaneFactory? VideoPaneFactory { get; set; }
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    public SessionSegmentStore SegmentStore { get; }
    public ISegmentCoordinator SegmentCoordinator { get; } = Substitute.For<ISegmentCoordinator>();
    public SessionOperationRunner OperationRunner { get; } = new();
    public IUiThreadDispatcher UiThreadDispatcher { get; set; } = new InlineUiThreadDispatcher();

    public static RecordedSessionDomainSnapshot DomainFromSnapshot(
        SessionSnapshot snapshot,
        SessionStaleness? staleness = null) => new(
        snapshot,
        null,
        null,
        null,
        null,
        null,
        staleness ?? new SessionStaleness.Current()) { Terrain = TestSnapshots.Terrain(snapshot) };

    public RecordedSessionDomainSnapshot WithTerrain(SessionSnapshot session, TerrainProfile profile)
    {
        var key = SessionTerrainStatusQuery.Evaluate(session, []).Key;
        TerrainProfiles.Seed(session.Id, key, profile);
        return DomainFromSnapshot(session) with
        {
            Terrain = new TerrainStatus.Current(key, TerrainProfileCalculator.Summarize(profile))
        };
    }

    /// <summary>
    /// Loads an editor over the segment-match telemetry and its track, with a rough window inside the
    /// 2–5 s analysis range, and runs Match over the given comparisons.
    /// </summary>
    public async Task<SessionDetailViewModel> LoadMatchedEditorAsync(SessionSnapshot session,
        IObservable<RecordedSessionDomainSnapshot>? laterDomains = null, params SegmentComparison[] matches)
    {
        var profile = Telemetry.Tests.TerrainProfiles.Classified(Telemetry.Tests.TerrainProfiles.Window(2, RoughnessClass.Rough, RoughnessSource.Both));
        var domains = (laterDomains ?? Observable.Empty<RecordedSessionDomainSnapshot>()).StartWith(WithTerrain(session, profile));
        var editor = CreateRecordedEditor(session, CreateLoadedResult(TestSegmentMatches.Telemetry(),
            trackPoints: [new(1000, 0, 0, null), new(1030, 200, 50, null)]), domains);
        await LoadAsync(editor);
        editor.SignalsWorkspace.SetAnalysisRange(2, 5);
        TestSegmentComparisons.MatchReturns(SegmentCoordinator, matches);
        await editor.SignalsWorkspace.SegmentComparison.MatchCommand.ExecuteAsync(null);
        return editor;
    }

    public SessionSnapshot CreateRecordedSession(
        string name = "Recorded Session 01",
        string description = "Suspension notes",
        bool hasProcessedData = true,
        long updated = 1) =>
        TestSnapshots.Session(name: name, description: description, hasProcessedData: hasProcessedData, updated: updated);

    public SessionDetailViewModel CreateRecordedEditor(
        SessionSnapshot? snapshot = null,
        SessionDetailLoadResult? loadResult = null,
        IObservable<RecordedSessionDomainSnapshot>? projectionChanges = null,
        bool deferDomainHandlingWhenInactive = true)
    {
        snapshot ??= CreateRecordedSession();
        ConfigureSnapshot(snapshot, projectionChanges);
        ConfigureLoad(snapshot, loadResult ?? CreateLoadedResult());
        return CreateEditor(snapshot, deferDomainHandlingWhenInactive);
    }

    public Task LoadAsync(SessionDetailViewModel editor) =>
        editor.LoadedCommand.ExecuteAsync(null);

    public Task SaveAsync(SessionDetailViewModel editor) =>
        editor.SaveCommand.ExecuteAsync(null);

    public Task DeleteAsync(SessionDetailViewModel editor, bool navigateBack = true) =>
        editor.DeleteCommand.ExecuteAsync(navigateBack);

    public Task CloseAsync(SessionDetailViewModel editor) => editor.PrepareCloseAsync();

    private void ConfigureSnapshot(SessionSnapshot snapshot, IObservable<RecordedSessionDomainSnapshot>? projectionChanges = null)
    {
        RecordedSessionProjection.WatchSession(snapshot.Id).Returns(projectionChanges ?? Observable.Empty<RecordedSessionDomainSnapshot>());
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
                    }));
            SessionLoader.LoadTrackAsync(
                    snapshot.Id,
                    presentation.TelemetryData,
                    Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                    Arg.Any<CancellationToken>())
                .Returns(new SessionDetailTrackLoadResult.Loaded(
                    new SessionTrackPresentationData(
                        presentation.FullTrackId,
                        presentation.FullTrackPoints,
                        presentation.TrackPoints)));
        }

        SessionLoader.LoadDetailAsync(
                snapshot.Id,
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(firstContentResult);
    }

    public SessionDetailLoadResult.Loaded CreateLoadedResult(
        TelemetryData? telemetry = null,
        bool includeBalance = true,
        IReadOnlyList<TrackPoint>? trackPoints = null)
    {
        var data = telemetry ?? TestTelemetryData.CreateProcessed(rearPresent: includeBalance);
        return new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                data,
                FullTrackId: null,
                FullTrackPoints: null,
                TrackPoints: trackPoints?.ToList())));
    }

    /// <summary>A loaded result for a synthetic ride with the full GPS track of its fixes.</summary>
    public static SessionDetailLoadResult.Loaded CreateLoadedRide(SyntheticRide ride)
    {
        var (track, points) = SyntheticRideTracks.FullTrack(ride, new SessionTrackTimeline(ride.Telemetry.Metadata.Timestamp));
        return new(new(new SessionTelemetryPresentationData(ride.Telemetry, track.Id, [.. points], [])));
    }

    private SessionDetailViewModel CreateEditor(
        SessionSnapshot snapshot,
        bool deferDomainHandlingWhenInactive)
    {
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(SessionInsightsService),
            new InlineBackgroundTaskRunner(),
            UiThreadDispatcher);
        var editor = new SessionDetailViewModel(
            snapshot,
            SessionCoordinator,
            SessionLoader,
            SessionStore,
            RecordedSessionProjection,
            new TestMapViewModelFactory(tileLayerService),
            Shell,
            DialogService,
            SessionPreferences,
            UiThreadDispatcher,
            deferDomainHandlingWhenInactive,
            ProcessedTelemetryReader,
            analysisResultStateFactory,
            SessionEditingCoordinator,
            TestSegmentPickers.CreateFactory(SegmentStore, SegmentCoordinator, DialogService),
            TestSegmentComparisons.CreateFactory(SegmentCoordinator),
            OperationRunner,
            videoPaneFactory: VideoPaneFactory,
            timeProvider: new FakeTimeProvider(), bikeCoordinator: BikeCoordinator, backgroundTaskRunner: BackgroundRunner,
            terrainProfiles: TerrainProfiles, terrainCoordinator: TerrainCoordinator);
        editor.SetTabActive(true);
        return editor;
    }
    public void Dispose()
    {
        SessionSummaries.Dispose();
        recordedPreferences.Dispose();
        SegmentStore.Dispose();
        publication.Dispose();
    }
}
